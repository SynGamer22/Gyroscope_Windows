using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net.Http;
using System.Text;
using MotionLinkVR.App;
using Xunit;

public class LanConnectionTests
{
    [Fact]
    public async Task Server_ReceivesMotionOnlyWithCorrectPairingCode()
    {
        var mobileRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../mobile"));
        var packetReceived = new TaskCompletionSource<MotionPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ports = GetTwoAvailablePorts();

        await using var server = await LanMotionServer.StartAsync(
            mobileRoot,
            packet => packetReceived.TrySetResult(packet),
            connected => clientConnected.TrySetResult(connected),
            ports.Http,
            ports.Https);

        using var httpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var httpClient = new HttpClient(httpHandler);
        var controllerPage = await httpClient.GetStringAsync($"https://127.0.0.1:{server.HttpsPort}/?code={server.PairingCode}");
        Assert.Contains("MotionLink VR", controllerPage);

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
}