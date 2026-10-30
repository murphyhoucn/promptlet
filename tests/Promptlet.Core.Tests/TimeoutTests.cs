using System.Net;
using System.Text;
using Promptlet.Core;
namespace Promptlet.Core.Tests;
public class TimeoutTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ContinuouslyArrivingDataCanTakeLongerThanOneIdlePeriod(bool stream)
    {
        var body = stream ? "data: {\"choices\":[{\"delta\":{\"content\":\"完整正文\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"
            : "{\"choices\":[{\"message\":{\"content\":\"完整正文\"},\"finish_reason\":\"stop\"}]}";
        using var http = new HttpClient(new Handler(body, stream, 40));
        var result = await new OptimizationService(http, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10))
            .OptimizeAsync(new() { BaseUrl = "http://localhost", Model = "mock", Stream = stream }, "input", _ => { }, CancellationToken.None);
        Assert.Equal("完整正文", result);
    }
    [Fact] public async Task StalledReadFailsWithoutWaitingForTotalTimeout()
    {
        using var http = new HttpClient(new Handler("{}", false, 3000));
        var exception = await Assert.ThrowsAsync<PromptletException>(() => new OptimizationService(http, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10))
            .OptimizeAsync(new() { BaseUrl = "http://localhost", Model = "mock", Stream = false }, "input", _ => { }, CancellationToken.None));
        Assert.Contains("超时", exception.Message);
    }
    private sealed class Handler(string body, bool stream, int delay) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new SlowStream(Encoding.UTF8.GetBytes(body), delay)) { Headers = { ContentType = new(stream ? "text/event-stream" : "application/json") } } });
    }
    private sealed class SlowStream(byte[] bytes, int delay) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(delay, token); return await base.ReadAsync(buffer[..Math.Min(8, buffer.Length)], token); }
    }
}
