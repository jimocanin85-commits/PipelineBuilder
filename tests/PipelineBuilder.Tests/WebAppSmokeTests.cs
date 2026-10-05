using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>Starts the real web app in memory and requests its pages.</summary>
public class WebAppSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebAppSmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task HomePageRenders()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        Assert.Contains("PipelineBuilder", html);
        Assert.Contains("What are you deploying?", html);
    }

    [Theory]
    [InlineData(@"_framework/blazor\.web(\.[a-z0-9]+)?\.js", "Blazor")]
    [InlineData(@"js/download(\.[a-z0-9]+)?\.js", "downloadText")]
    [InlineData(@"app(\.[a-z0-9]+)?\.css", ".wizard-nav")]
    public async Task AssetsReferencedByThePageAreServed(string pattern, string expected)
    {
        // .NET 10 serves static assets with fingerprinted URLs; follow the URL the page actually uses.
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        var match = System.Text.RegularExpressions.Regex.Match(html, "(src|href)=\"(?<url>" + pattern + ")\"");
        Assert.True(match.Success, $"No reference matching {pattern} in the page");

        var response = await client.GetAsync("/" + match.Groups["url"].Value);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/js/download.js", "downloadText")]
    [InlineData("/js/download.js", "toggleTheme")]
    [InlineData("/app.css", ".wizard-nav")]
    [InlineData("/app.css", "[data-theme=\"dark\"]")]
    public async Task StaticAssetsAreServed(string path, string expected)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ErrorPageExists()
    {
        var response = await _factory.CreateClient().GetAsync("/Error");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
