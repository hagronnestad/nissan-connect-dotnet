using NissanConnectLib.Api;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NissanConnectLib.Tests;

public class HttpRequestClonerTests
{
    [Fact]
    public async Task CloneAsync_PreservesBodyHeadersVersionAndOptions()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/action")
        {
            Content = JsonContent.Create(new { data = new { type = "RefreshBatteryStatus" } }),
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var option = new HttpRequestOptionsKey<string>("test-option");
        request.Options.Set(option, "preserved");
        var body = await request.Content.ReadAsStringAsync();

        using var clone = await HttpRequestCloner.CloneAsync(request, CancellationToken.None);
        request.Dispose();

        Assert.Equal(HttpMethod.Post, clone.Method);
        Assert.Equal(new Uri("https://example.test/action"), clone.RequestUri);
        Assert.Equal(HttpVersion.Version20, clone.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, clone.VersionPolicy);
        Assert.Equal("test-token", clone.Headers.Authorization?.Parameter);
        Assert.True(clone.Options.TryGetValue(option, out var value));
        Assert.Equal("preserved", value);
        Assert.NotNull(clone.Content);
        Assert.Equal("application/json", clone.Content.Headers.ContentType?.MediaType);
        Assert.Equal(body, await clone.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CloneAsync_CanExcludeContentForRedirects()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/action")
        {
            Content = new StringContent("form-body")
        };

        using var clone = await HttpRequestCloner.CloneAsync(request, CancellationToken.None, includeContent: false);

        Assert.Null(clone.Content);
        Assert.Equal("form-body", await request.Content.ReadAsStringAsync());
    }
}
