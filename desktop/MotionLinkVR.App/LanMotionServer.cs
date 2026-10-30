using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace MotionLinkVR.App;

public sealed record MotionPacket(
    string Type,
    double Yaw = 0,
    double Pitch = 0,
    double Roll = 0,
    long Timestamp = 0,
    double Sensitivity = 1,
    double Smoothing = 0,
    double Deadzone = 0);

public sealed class LanMotionServer : IAsyncDisposable
{
    public const int DefaultHttpPort = 47820;
    public const int DefaultHttpsPort = 47821;
    public const int DefaultDiscoveryPort = 47822;

    private readonly WebApplication _application;
    private readonly X509Certificate2 _serverCertificate;
    private readonly X509Certificate2 _rootCertificate;
    private readonly Action<MotionPacket> _onMotion;
    private readonly Action<bool> _onConnection;
    private readonly string _pairingCode;
    private readonly LanDiscoveryService _discoveryService;
    private int _connectedClients;

    public IPAddress Address { get; }
    public int HttpPort { get; }
    public int HttpsPort { get; }
    public int DiscoveryPort { get; }
    public string PairingCode => _pairingCode;
    public string ControllerUrl => $"https://{Address}:{HttpsPort}/?code={_pairingCode}";
    public string CertificateUrl => $"http://{Address}:{HttpPort}/root-ca.cer";
    public string CertificateFingerprint => Convert.ToHexString(SHA256.HashData(_rootCertificate.RawData));

    private LanMotionServer(
        WebApplication application,
        X509Certificate2 rootCertificate,
        X509Certificate2 serverCertificate,
        IPAddress address,
        int httpPort,
        int httpsPort,
        int discoveryPort,
        LanDiscoveryService discoveryService,
        string pairingCode,
        Action<MotionPacket> onMotion,
        Action<bool> onConnection)
    {
        _application = application;
        _rootCertificate = rootCertificate;
        _serverCertificate = serverCertificate;
        Address = address;
        HttpPort = httpPort;
        HttpsPort = httpsPort;
        DiscoveryPort = discoveryPort;
        _discoveryService = discoveryService;
        _pairingCode = pairingCode;
        _onMotion = onMotion;
        _onConnection = onConnection;
    }

    public static async Task<LanMotionServer> StartAsync(
        string mobileRoot,
        Action<MotionPacket> onMotion,
        Action<bool> onConnection,
        int httpPort = DefaultHttpPort,
        int httpsPort = DefaultHttpsPort,
        int discoveryPort = DefaultDiscoveryPort,
        CancellationToken cancellationToken = default)
    {
        var webRoot = Path.GetFullPath(mobileRoot);
        if (!Directory.Exists(webRoot))
        {
            throw new DirectoryNotFoundException($"Mobile site files were not found at {webRoot}.");
        }

        var address = GetLanAddress();
        var (rootCertificate, serverCertificate) = LanCertificateProvider.CreateServerCertificate(address);
        var pairingCode = CreatePairingCode();
        LanDiscoveryService discoveryService;
        try
        {
            discoveryService = new LanDiscoveryService(address, httpPort, httpsPort, discoveryPort);
        }
        catch
        {
            serverCertificate.Dispose();
            rootCertificate.Dispose();
            throw;
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = []
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(httpPort);
            options.ListenAnyIP(httpsPort, listen => listen.UseHttps(serverCertificate));
        });

        var application = builder.Build();
        application.UseWebSockets();

        application.MapGet("/root-ca.cer", async context =>
        {
            context.Response.ContentType = "application/pkix-cert";
            context.Response.Headers.ContentDisposition = "attachment; filename=MotionLinkVR-Local-CA.cer";
            await context.Response.Body.WriteAsync(rootCertificate.Export(X509ContentType.Cert), context.RequestAborted);
        });
        application.MapGet("/health", () => Results.Ok(new { status = "ready" }));

        var server = new LanMotionServer(
            application,
            rootCertificate,
            serverCertificate,
            address,
            httpPort,
            httpsPort,
            discoveryPort,
            discoveryService,
            pairingCode,
            onMotion,
            onConnection);

        application.MapGet("/motion", async context =>
        {
            if (!string.Equals(context.Request.Query["code"], pairingCode, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            if (Interlocked.Increment(ref server._connectedClients) == 1)
            {
                server._onConnection(true);
            }

            try
            {
                await ReceiveMotionAsync(socket, server._onMotion, context.RequestAborted);
            }
            finally
            {
                if (Interlocked.Decrement(ref server._connectedClients) == 0)
                {
                    server._onConnection(false);
                }
            }
        });

        var provider = new PhysicalFileProvider(webRoot);
        application.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        application.UseStaticFiles(new StaticFileOptions { FileProvider = provider });

        try
        {
            await application.StartAsync(cancellationToken);
            discoveryService.Start();
        }
        catch
        {
            await application.DisposeAsync();
            await discoveryService.DisposeAsync();
            serverCertificate.Dispose();
            rootCertificate.Dispose();
            throw;
        }

        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _discoveryService.DisposeAsync();
        await _application.StopAsync();
        await _application.DisposeAsync();
        _serverCertificate.Dispose();
        _rootCertificate.Dispose();
    }

    private static async Task ReceiveMotionAsync(
        WebSocket socket,
        Action<MotionPacket> onMotion,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[2048];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    return;
                }

                if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 8192)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Invalid motion packet", CancellationToken.None);
                    return;
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            try
            {
                var packet = JsonSerializer.Deserialize<MotionPacket>(
                    message.GetBuffer().AsSpan(0, (int)message.Length),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (packet is not null
                    && (packet.Type switch
                    {
                        "motion" => double.IsFinite(packet.Yaw)
                            && double.IsFinite(packet.Pitch)
                            && double.IsFinite(packet.Roll)
                            && Math.Abs(packet.Yaw) <= 360
                            && Math.Abs(packet.Pitch) <= 360
                            && Math.Abs(packet.Roll) <= 360,
                        "settings" => double.IsFinite(packet.Sensitivity)
                            && double.IsFinite(packet.Smoothing)
                            && double.IsFinite(packet.Deadzone),
                        "pause" or "resume" or "recenter" => true,
                        _ => false
                    }))
                {
                    onMotion(packet);
                }
            }
            catch (JsonException)
            {
                // Ignore malformed sensor packets and keep the live connection available.
            }
        }
    }

    private static string CreatePairingCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = RandomNumberGenerator.GetBytes(8);
        return string.Concat(random.Select(value => alphabet[value % alphabet.Length]));
    }

    private static IPAddress GetLanAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsUsableLanInterface)
            .SelectMany(item => item.GetIPProperties().UnicastAddresses
                .Select(address => new LanAddressCandidate(
                    address.Address,
                    item.NetworkInterfaceType,
                    item.GetIPProperties().GatewayAddresses.Count > 0,
                    IsPrivateAddress(address.Address))))
            .Where(candidate => IsUsableLanAddress(candidate.Address))
            .ToArray();

        return SelectPreferredLanAddress(candidates) ?? IPAddress.Loopback;
    }

    public static IPAddress SelectPreferredLanAddress(IEnumerable<LanAddressCandidate> candidates)
    {
        return candidates
            .Where(candidate => IsUsableLanAddress(candidate.Address))
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenByDescending(candidate => candidate.InterfaceType == NetworkInterfaceType.Wireless80211)
            .ThenByDescending(candidate => candidate.InterfaceType == NetworkInterfaceType.Ethernet)
            .ThenByDescending(candidate => candidate.IsPrivateAddress)
            .Select(candidate => candidate.Address)
            .FirstOrDefault() ?? IPAddress.Loopback;
    }

    private static bool IsUsableLanInterface(NetworkInterface item)
    {
        if (item.OperationalStatus != OperationalStatus.Up)
        {
            return false;
        }

        if (item.NetworkInterfaceType is NetworkInterfaceType.Loopback
            or NetworkInterfaceType.Tunnel
            or NetworkInterfaceType.Ppp)
        {
            return false;
        }

        var name = item.Name ?? string.Empty;
        var description = item.Description ?? string.Empty;
        return !name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("VMware", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("Docker", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("WSL", StringComparison.OrdinalIgnoreCase)
            && !description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            && !description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            && !description.Contains("TAP", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsableLanAddress(IPAddress address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            && bytes[0] != 0
            && bytes[0] != 127
            && !(bytes[0] == 169 && bytes[1] == 254)
            && bytes[0] != 255;
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
    }

    public readonly record struct LanAddressCandidate(
        IPAddress Address,
        NetworkInterfaceType InterfaceType,
        bool HasGateway,
        bool IsPrivateAddress);
}

internal sealed class LanDiscoveryService : IAsyncDisposable
{
    internal const string DiscoveryRequest = "MOTIONLINK_DISCOVER_V1";

    private readonly UdpClient _udpClient;
    private readonly byte[] _response;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _receiveTask;

    public LanDiscoveryService(IPAddress address, int httpPort, int httpsPort, int discoveryPort)
    {
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, discoveryPort));
        _response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            service = "MotionLinkVR",
            address = address.ToString(),
            httpPort,
            httpsPort
        });
    }

    public void Start()
    {
        _receiveTask = ReceiveAsync(_cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
        }

        _udpClient.Dispose();
        _cancellation.Dispose();
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await _udpClient.ReceiveAsync(cancellationToken);
            if (!string.Equals(
                    Encoding.UTF8.GetString(request.Buffer),
                    DiscoveryRequest,
                    StringComparison.Ordinal))
            {
                continue;
            }

            await _udpClient.SendAsync(_response, request.RemoteEndPoint, cancellationToken);
        }
    }
}

internal static class LanCertificateProvider
{
    private static readonly string CertificateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MotionLinkVR",
        "certificates");

    public static (X509Certificate2 RootCertificate, X509Certificate2 ServerCertificate) CreateServerCertificate(IPAddress address)
    {
        Directory.CreateDirectory(CertificateDirectory);
        var rootPath = Path.Combine(CertificateDirectory, "local-root.pfx");
        var passwordPath = Path.Combine(CertificateDirectory, "local-root-password.txt");
        var publicRootPath = Path.Combine(CertificateDirectory, "local-root.cer");

        X509Certificate2 root;
        if (File.Exists(rootPath) && File.Exists(passwordPath))
        {
            var password = File.ReadAllText(passwordPath);
            root = X509CertificateLoader.LoadPkcs12FromFile(rootPath, password, X509KeyStorageFlags.Exportable);
        }
        else
        {
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=MotionLink VR Local CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(10));
            File.WriteAllText(passwordPath, password);
            File.WriteAllBytes(rootPath, root.Export(X509ContentType.Pfx, password));
            File.WriteAllBytes(publicRootPath, root.Export(X509ContentType.Cert));
        }

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=MotionLink VR LAN", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(address);
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        serverRequest.CertificateExtensions.Add(san.Build());

        using var publicServerCertificate = serverRequest.Create(
            root,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(2),
            RandomNumberGenerator.GetBytes(16));

        return (root, publicServerCertificate.CopyWithPrivateKey(serverKey));
    }
}