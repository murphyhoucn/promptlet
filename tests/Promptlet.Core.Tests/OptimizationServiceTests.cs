using System.Net;
using System.Text;
using System.Text.Json;
using Promptlet.Core;

namespace Promptlet.Core.Tests;

public class OptimizationServiceTests
{
    private static readonly AppSettings Settings = new() { BaseUrl = "https://example.com/v1", ApiKey = "test-secret", Model = "test-model" };

    [Fact]
    public async Task StreamHandlesSplitUtf8CommentsMultilineEventsAndUsage()
    {
        var events = ": keepalive\r\n\r\n" +
            "data: {\"choices\": [\r\ndata: {\"delta\":{\"content\":\"你好🌱\"},\"finish_reason\":null}]}\r\n\r\n" +
            Event("，请明确目标。") +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: {\"choices\":[],\"usage\":{\"total_tokens\":10}}\n\n" + "data: [DONE]\n\n";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BytewiseStream(Encoding.UTF8.GetBytes(events)))
            { Headers = { ContentType = new("text/event-stream") } }
        });
        using var http = new HttpClient(handler);
        var progress = new List<string>();
        var result = await new OptimizationService(http).OptimizeAsync(Settings, "原始内容", progress.Add, CancellationToken.None);
        Assert.Equal("你好🌱，请明确目标。", result);
        Assert.Equal(result, progress[^1]);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"部分\"},\"finish_reason\":null}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"部分\"},\"finish_reason\":\"length\"}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n")]
    [InlineData("data: [DONE]\n\n")]
    [InlineData("data: {invalid}\n\n")]
    public async Task InvalidOrIncompleteStreamsNeverSucceed(string events)
    {
        using var http = new HttpClient(new StubHandler(_ => Sse(events)));
        await Assert.ThrowsAsync<PromptletException>(() => new OptimizationService(http)
            .OptimizeAsync(Settings, "input", _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task NonStreamingUsesCorrectRequestAndReadsOnlyContent()
    {
        string? requestBody = null;
        string? auth = null;
        Uri? endpoint = null;
        var handler = new StubHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            auth = request.Headers.Authorization?.ToString();
            endpoint = request.RequestUri;
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"choices":[{"message":{"content":"优化结果","reasoning_content":"不要展示"},"finish_reason":"stop"}]}
                """, Encoding.UTF8, "application/json") };
        });
        using var http = new HttpClient(handler);
        var result = await new OptimizationService(http).OptimizeAsync(Settings with { Stream = false }, "原文", _ => { }, CancellationToken.None);
        Assert.Equal("优化结果", result);
        Assert.Equal("Bearer test-secret", auth);
        Assert.Equal("https://example.com/v1/chat/completions", endpoint!.AbsoluteUri);
        using var body = JsonDocument.Parse(requestBody!);
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("原文", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task NoKeyMeansNoAuthorizationHeader()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return Sse(Event("文本") + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n");
        });
        using var http = new HttpClient(handler);
        Assert.Equal("文本", await new OptimizationService(http).OptimizeAsync(
            Settings with { BaseUrl = "http://localhost:3000", ApiKey = "" }, "input", _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticationFailureIsSanitizedAndNotRetried()
    {
        var handler = new StubHandler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("private-body-with-secret") });
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<PromptletException>(() => new OptimizationService(http)
            .OptimizeAsync(Settings, "input", _ => { }, CancellationToken.None));
        Assert.DoesNotContain("private-body", error.Message);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutBecomingTimeout()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        using var http = new HttpClient(new StubHandler(_ => Sse("")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OptimizationService(http)
            .OptimizeAsync(Settings, "input", _ => { }, token.Token));
    }

    private static string Event(string text) => "data: " + JsonSerializer.Serialize(new
    {
        choices = new[] { new { delta = new { content = text }, finish_reason = (string?)null } }
    }) + "\n\n";
    private static HttpResponseMessage Sse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Count++; return Task.FromResult(respond(request)); }
    }

    private sealed class BytewiseStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer, offset, Math.Min(1, count), cancellationToken);
    }
}
