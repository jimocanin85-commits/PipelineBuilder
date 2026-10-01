using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

namespace PipelineBuilder.Web.Security;

/// <summary>The <c>Authentication</c> section of appsettings.</summary>
public sealed class AuthenticationSettings
{
    /// <summary>
    /// <c>Windows</c> or <c>None</c>. When empty, Windows login is on everywhere except in
    /// Development, so a published app is never open by accident while <c>dotnet run</c> stays simple.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// Optional Active Directory groups (e.g. <c>CONTOSO\Platform-Team</c>). When set, only members
    /// of at least one of them can use the app; otherwise any signed-in Windows user can.
    /// </summary>
    public string[] AllowedGroups { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Windows login (Negotiate: Kerberos or NTLM). On IIS the handler uses IIS's own Windows
/// Authentication; on Kestrel it negotiates itself.
/// </summary>
public static class WindowsAuthentication
{
    public static bool IsEnabled(AuthenticationSettings settings, bool isDevelopment) =>
        settings.Mode?.Trim().ToUpperInvariant() switch
        {
            "WINDOWS" => true,
            "NONE" => false,
            null or "" => !isDevelopment,
            var other => throw new InvalidOperationException($"Authentication:Mode '{other}' is not supported. Use 'Windows' or 'None'.")
        };

    /// <summary>Every request needs a signed-in user, and a member of an allowed group if any are configured.</summary>
    public static AuthorizationPolicy BuildPolicy(AuthenticationSettings settings)
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
        var groups = settings.AllowedGroups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToArray();
        if (groups.Length > 0)
            policy.RequireRole(groups); // Windows group memberships are exposed as roles
        return policy.Build();
    }

    /// <summary>Registers authentication and authorization; returns whether Windows login is on.</summary>
    public static bool AddPipelineBuilderAuthentication(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("Authentication").Get<AuthenticationSettings>() ?? new AuthenticationSettings();
        var enabled = IsEnabled(settings, builder.Environment.IsDevelopment());

        if (enabled)
        {
            builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
            builder.Services.AddAuthorization(options => options.FallbackPolicy = BuildPolicy(settings));
        }
        else
        {
            builder.Services.AddAuthorization();
        }

        builder.Services.AddCascadingAuthenticationState();
        return enabled;
    }
}
