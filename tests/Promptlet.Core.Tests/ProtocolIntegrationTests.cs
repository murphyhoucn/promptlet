using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Promptlet.Core;

namespace Promptlet.Core.Tests;

public class ProtocolIntegrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealLoopbackHttpReachesProtectedClipboardCommit(bool streaming)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeOnceAsync(listener, streaming);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        var clipboard = new MemoryClipboard();
        var coordinator = new OptimizationCoordinator(clipboard, new OptimizationService(client));
        await coordinator.ActivateAsync(new AppSettings
        {
            BaseUrl = $"http://127.0.0.1:{port}/v1", Model = "fixture", Stream = streaming
        }).WaitAsync(TimeSpan.FromSeconds(10));
        var request = await server.WaitAsync(TimeSpan.FromSeconds(5));
        using var requestBody = JsonDocument.Parse(request);
        Assert.Equal("请整理这段想法", requestBody.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("请明确目标与输出格式。", clipboard.Text);
        Assert.Equal(1, clipboard.Writes);
        Assert.Equal(OptimizationStatus.Copied, coordinator.State.Status);
    }

    private static async Task<string> ServeOnceAsync(TcpListener listener, bool streaming)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
        using var stream = connection.GetStream();
        var headerBytes = new List<byte>();
        var single = new byte[1];
        while (headerBytes.Count < 16_384)
        {
            Assert.Equal(1, await stream.ReadAsync(single, timeout.Token));
            headerBytes.Add(single[0]);
            if (headerBytes.Count >= 4 && headerBytes.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
        }
        var headers = Encoding.ASCII.GetString(headerBytes.ToArray());
        Assert.StartsWith("POST /v1/chat/completions HTTP/1.1", headers);
        Assert.DoesNotContain("Authorization:", headers, StringComparison.OrdinalIgnoreCase);
        var lengthLine = headers.Split("\r\n").Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var bodyBytes = new byte[int.Parse(lengthLine.Split(':')[1].Trim())];
        await stream.ReadExactlyAsync(bodyBytes, timeout.Token);
        var response = streaming
            ? "data: {\"choices\":[{\"delta\":{\"content\":\"请明确目标与输出格式。\"},\"finish_reason\":null}]}\n\n" +
              "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"
            : "{\"choices\":[{\"message\":{\"content\":\"请明确目标与输出格式。\"},\"finish_reason\":\"stop\"}]}";
        var responseBody = Encoding.UTF8.GetBytes(response);
        var responseHeader = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {(streaming ? "text/event-stream" : "application/json")}\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHeader, timeout.Token);
        await stream.WriteAsync(responseBody, timeout.Token);
        return Encoding.UTF8.GetString(bodyBytes);
    }

    private sealed class MemoryClipboard : IClipboardService
    {
        public string Text { get; private set; } = "请整理这段想法";
        public int Writes { get; private set; }
        public Task<ClipboardSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(new ClipboardSnapshot(Text, 1));
        public Task<ClipboardWriteResult> TryWriteAsync(string text, ClipboardSnapshot expected, CancellationToken token)
        {
            Assert.Equal("请整理这段想法", expected.Text);
            Text = text; Writes++;
            return Task.FromResult(ClipboardWriteResult.Written);
        }
        public Task WriteAsync(string text, CancellationToken token) => throw new NotSupportedException();
    }
}
