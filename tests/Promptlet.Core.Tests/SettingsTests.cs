using System.Text.Json;
using Promptlet.Core;

namespace Promptlet.Core.Tests;

public class SettingsTests
{
    [Theory]
    [InlineData("https://example.com/v1", "https://example.com/v1/chat/completions")]
    [InlineData("https://example.com/custom/", "https://example.com/custom/chat/completions")]
    [InlineData("https://example.com/chat/completions/", "https://example.com/chat/completions")]
    [InlineData("http://127.0.0.1:8000/v1", "http://127.0.0.1:8000/v1/chat/completions")]
    [InlineData("http://[::1]:8000/v1", "http://[::1]:8000/v1/chat/completions")]
    public void EndpointPreservesPrefixAndAvoidsDuplication(string source, string expected) =>
        Assert.Equal(expected, new AppSettings { BaseUrl = source }.GetEndpoint().AbsoluteUri);

    [Theory]
    [InlineData("http://example.com/v1")]
    [InlineData("https://example.com/v1?key=secret")]
    [InlineData("https://key@example.com/v1")]
    [InlineData("https://example.com/#secret")]
    [InlineData("file:///tmp/model")]
    [InlineData("")]
    public void EndpointRejectsInvalidAddresses(string source) =>
        Assert.Throws<PromptletException>(() => new AppSettings { BaseUrl = source }.GetEndpoint());

    [Fact]
    public void KeyIsNeverSerializedIntoOrdinarySettings()
    {
        var json = JsonSerializer.Serialize(new AppSettings { ApiKey = "do-not-store-this-key" });
        Assert.DoesNotContain("do-not-store-this-key", json);
        Assert.DoesNotContain("ApiKey", json);
        Assert.DoesNotContain("SystemPrompt", json);
    }

    [Fact]
    public void LocalModelCanWorkWithoutAKey() =>
        new AppSettings { BaseUrl = "http://localhost:1234/v1", Model = "local" }.ValidateConnection();
}
