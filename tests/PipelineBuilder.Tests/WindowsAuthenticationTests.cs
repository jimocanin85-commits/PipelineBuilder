using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using PipelineBuilder.Web.Security;
using Xunit;

namespace PipelineBuilder.Tests;

public class WindowsAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WindowsAuthenticationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData(null, true, false)]       // dotnet run: open
    [InlineData(null, false, true)]       // published (Production): Windows login
    [InlineData("", false, true)]
    [InlineData("Windows", true, true)]
    [InlineData("windows", true, true)]
    [InlineData("None", false, false)]
    public void WindowsLoginIsOnOutsideDevelopmentUnlessTurnedOff(string? mode, bool isDevelopment, bool expected)
    {
        Assert.Equal(expected, WindowsAuthentication.IsEnabled(new AuthenticationSettings { Mode = mode }, isDevelopment));
    }

    [Fact]
    public void UnknownModeIsAConfigurationError()
    {
        Assert.Throws<InvalidOperationException>(() => WindowsAuthentication.IsEnabled(new AuthenticationSettings { Mode = "Basic" }, false));
    }

    [Fact]
    public void PolicyRequiresASignedInUser()
    {
        var policy = WindowsAuthentication.BuildPolicy(new AuthenticationSettings());

        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.DoesNotContain(policy.Requirements, r => r is RolesAuthorizationRequirement);
    }

    [Fact]
    public void AllowedGroupsBecomeARoleRequirement()
    {
        var policy = WindowsAuthentication.BuildPolicy(new AuthenticationSettings { AllowedGroups = new[] { @"CONTOSO\Platform", " ", @"CONTOSO\Ops " } });

        var roles = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(new[] { @"CONTOSO\Platform", @"CONTOSO\Ops" }, roles.AllowedRoles);
    }

    [Fact]
    public async Task WithWindowsLoginAnonymousRequestsGetANegotiateChallenge()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Authentication:Mode", "Windows"));

        var response = await factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Negotiate", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task WithoutWindowsLoginThePageIsOpen()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Authentication:Mode", "None"));

        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("Project type", html);
        Assert.DoesNotContain("Signed in as", html);
    }
}
