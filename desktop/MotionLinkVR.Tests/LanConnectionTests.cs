using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MotionLinkVR.App;
using Xunit;

public class LanConnectionTests
{
    [Fact]
    public void SelectPreferredLanAddress_PrefersGatewayedWirelessInterface()
    {
        var selected = LanMotionServer.SelectPreferredLanAddress([
            new LanMotionServer.LanAddressCandidate(IPAddress.Parse("10.0.0.5"), NetworkInterfaceType.Unknown, false, true),
            new LanMotionServer.LanAddressCandidate(IPAddress.Parse("169.254.12.34"), NetworkInterfaceType.Wireless80211, true, false),
            new LanMotionServer.LanAddressCandidate(IPAddress.Parse("192.168.1.42"), NetworkInterfaceType.Wireless80211, true, true),
            new LanMotionServer.LanAddressCandidate(IPAddress.Parse("172.25.8.7"), NetworkInterfaceType.Ethernet, true, true)
        ]);

        Assert.Equal(IPAddress.Parse("192.168.1.42"), selected);
    }

    [Fact]
    public void SelectPreferredLanAddress_PrefersWifiOverOtherGatewayedInterfaces()
    {
        var selected = LanMotionServer.SelectPreferredLanAddress([
            new LanMotionServer.LanAddressCandidate(IPAddress.Parse("10.20.30.40"), NetworkInterfaceType.Ethernet, true, true),
            new LanMotionServer.LanAddressCandidate(IPAddress.Parse("192.168.1.42"), NetworkInterfaceType.Wireless80211, false, true)
        ]);

        Assert.Equal(IPAddress.Parse("192.168.1.42"), selected);
    }

    [Fact]
    public async Task Server_ReceivesMotionOnlyWithCorrectPairingCode()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../mobile"));
        var packetReceived = new TaskCompletionSource<MotionPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ports = GetTwoAvailablePorts();
        var discoveryPort = GetAvailableUdpPort();

        await using var server = await LanMotionServer.StartAsync(
            mobileRoot,
            packet => packetReceived.TrySetResult(packet),
            connected => clientConnected.TrySetResult(connected),
            ports.Http,
            ports.Https,
            discoveryPort);

        using (var discoveryClient = new UdpClient(AddressFamily.InterNetwork))
        using (var discoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var request = Encoding.UTF8.GetBytes("MOTIONLINK_DISCOVER_V1");
            await discoveryClient.SendAsync(
                request,
                new IPEndPoint(IPAddress.Loopback, server.DiscoveryPort),
                discoveryTimeout.Token);
            var response = await discoveryClient.ReceiveAsync(discoveryTimeout.Token);
            using var discovery = JsonDocument.Parse(response.Buffer);

            Assert.Equal("MotionLinkVR", discovery.RootElement.GetProperty("service").GetString());
            Assert.Equal(server.Address.ToString(), discovery.RootElement.GetProperty("address").GetString());
            Assert.Equal(server.HttpPort, discovery.RootElement.GetProperty("httpPort").GetInt32());
            Assert.Equal(server.HttpsPort, discovery.RootElement.GetProperty("httpsPort").GetInt32());
        }

        using var httpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var httpClient = new HttpClient(httpHandler);
        var controllerPage = await httpClient.GetStringAsync($"https://127.0.0.1:{server.HttpsPort}/?code={server.PairingCode}");
        Assert.Contains("MotionLink VR", controllerPage);
        var healthResponse = await httpClient.GetStringAsync($"http://127.0.0.1:{server.HttpPort}/health");
        Assert.Contains("\"status\":\"ready\"", healthResponse);

        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uri = new UriBuilder("wss", "127.0.0.1", server.HttpsPort, "/motion")
        {
            Query = $"code={Uri.EscapeDataString(server.PairingCode)}"
        }.Uri;

        using (var wrongCodeSocket = new ClientWebSocket())
        {
            wrongCodeSocket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            var wrongCodeUri = new UriBuilder(uri) { Query = "code=NOT-THE-CODE" }.Uri;
            await Assert.ThrowsAsync<WebSocketException>(() => wrongCodeSocket.ConnectAsync(wrongCodeUri, timeout.Token));
        }

        await socket.ConnectAsync(uri, timeout.Token);
        Assert.True(await clientConnected.Task.WaitAsync(timeout.Token));

        var json = "{\"type\":\"motion\",\"yaw\":12.5,\"pitch\":-4.25,\"roll\":1.5,\"timestamp\":123456789}";
        await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, timeout.Token);

        var packet = await packetReceived.Task.WaitAsync(timeout.Token);
        Assert.Equal("motion", packet.Type);
        Assert.Equal(12.5, packet.Yaw);
        Assert.Equal(-4.25, packet.Pitch);
        Assert.Equal(1.5, packet.Roll);
        Assert.InRange(
            packet.Timestamp,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 5000,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", timeout.Token);
    }

    private static (int Http, int Https) GetTwoAvailablePorts()
    {
        using var first = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        var http = ((IPEndPoint)first.LocalEndpoint).Port;
        using var second = new TcpListener(IPAddress.Loopback, 0);
        second.Start();
        var https = ((IPEndPoint)second.LocalEndpoint).Port;
        return (http, https);
    }

    private static int GetAvailableUdpPort()
    {
        using var udpClient = new UdpClient(0);
        return ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
    }
}