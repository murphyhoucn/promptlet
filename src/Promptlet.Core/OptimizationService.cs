using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Promptlet.Core;

public sealed class OptimizationService(HttpClient client, TimeSpan? stallTimeout = null, TimeSpan? totalTimeout = null) : IOptimizationService
{
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);
    private const int MaxOutputChars = 256_000;

    public async Task<string> OptimizeAsync(AppSettings settings, string input, Action<string> onText, CancellationToken cancellationToken)
    {
        settings.ValidateConnection();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(totalTimeout ?? TotalTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.GetEndpoint());
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(settings.Stream ? "text/event-stream" : "application/json"));
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = settings.Model.Trim(),
                stream = settings.Stream,
                messages = new[] {
                    new { role = "system", content = settings.SystemPrompt },
                    new { role = "user", content = input }
                }
            }), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw HttpError(response.StatusCode);
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var protectedStream = new StallProtectedStream(stream, stallTimeout ?? StallTimeout);
            using var reader = new StreamReader(protectedStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            if (!settings.Stream)
            {
                var json = await ReadBoundedAsync(reader, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var choice = GetChoice(document.RootElement);
                EnsureStop(choice);
                if (!choice.TryGetProperty("message", out var message) ||
                    !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                    throw new PromptletException("模型未返回文本正文，请检查模型与接口格式。");
                var text = RequireText(content.GetString());
                onText(text);
                return text;
            }

            if (response.Content.Headers.ContentType?.MediaType is not "text/event-stream")
                throw new PromptletException("服务未返回 SSE 流，请在设置中关闭流式输出后重试。");

            return await ReadEventsAsync(reader, onText, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PromptletException("请求超时或响应停滞，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            throw new PromptletException("无法连接模型服务，请检查接口地址、网络或代理设置。");
        }
        catch (IOException)
        {
            throw new PromptletException("连接中断，未复制不完整的结果。请重试。");
        }
        catch (JsonException)
        {
            throw new PromptletException("响应格式不兼容，请检查模型或关闭流式输出后重试。");
        }
    }

    private static async Task<string> ReadEventsAsync(StreamReader reader, Action<string> onText, CancellationToken token)
    {
        var eventData = new StringBuilder();
        var output = new StringBuilder();
        var stopped = false;
        var done = false;
        while (!done)
        {
            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                // An unterminated event is a truncated response, even if its JSON parses.
                if (eventData.Length != 0 || !stopped)
                    throw new PromptletException("响应意外结束，未复制不完整的结果。请重试。");
                break;
            }
            if (line.Length > MaxOutputChars) throw new PromptletException("服务返回的单个事件过大。");
            if (line.Length == 0)
            {
                if (eventData.Length == 0) continue;
                var payload = eventData.ToString().TrimEnd('\n');
                eventData.Clear();
                if (payload == "[DONE]")
                {
                    if (!stopped) throw new PromptletException("模型未报告正常结束，未自动复制。请检查接口兼容性。");
                    done = true;
                    continue;
                }
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out _)) throw new PromptletException("模型服务返回错误，请检查配置或稍后重试。");
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                    throw new PromptletException("响应格式不兼容，请关闭流式输出后重试。");
                // Usage-only final events carry an empty choices array.
                if (choices.GetArrayLength() == 0) continue;
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var part) && part.ValueKind == JsonValueKind.String)
                {
                    var text = part.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        if (stopped) throw new PromptletException("服务在结束标记后仍返回正文，未自动复制。");
                        output.Append(text);
                        if (output.Length > MaxOutputChars) throw new PromptletException("结果过长，已停止接收。");
                        onText(output.ToString());
                    }
                }
                if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind != JsonValueKind.Null)
                {
                    EnsureStop(choice);
                    stopped = true;
                }
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var data = line.AsSpan(5);
                if (data.StartsWith(" ")) data = data[1..];
                eventData.Append(data).Append('\n');
                if (eventData.Length > MaxOutputChars) throw new PromptletException("服务返回的单个事件过大。");
            }
        }
        return RequireText(output.ToString());
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > MaxOutputChars * 2) throw new PromptletException("服务响应过大，已停止接收。");
        }
        return text.ToString();
    }

    private static JsonElement GetChoice(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new PromptletException("服务未返回有效结果，请检查接口格式。");
        return choices[0];
    }

    private static void EnsureStop(JsonElement choice)
    {
        if (!choice.TryGetProperty("finish_reason", out var finish) || finish.ValueKind != JsonValueKind.String || finish.GetString() != "stop")
            throw new PromptletException("结果被截断、过滤或未正常结束，未自动复制。请调整输入或模型后重试。");
    }

    private static string RequireText(string? text) => string.IsNullOrWhiteSpace(text)
        ? throw new PromptletException("模型返回了空结果，请重试或更换模型。") : text;

    private static PromptletException HttpError(HttpStatusCode code) => new(code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "鉴权失败，请检查 API Key 和模型访问权限。",
        HttpStatusCode.NotFound => "接口或模型不存在，请检查 Base URL 与模型名称。",
        HttpStatusCode.TooManyRequests => "请求受到限流或额度不足，请稍后重试并检查服务额度。",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "服务不接受当前请求，请检查模型和接口配置。",
        >= HttpStatusCode.InternalServerError => "模型服务暂时不可用，请稍后重试。",
        _ => $"模型请求失败（HTTP {(int)code}），请检查配置后重试。"
    });
}
