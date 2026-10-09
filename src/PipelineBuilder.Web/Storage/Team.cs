using System.Data.Common;
using Microsoft.AspNetCore.Components.Authorization;

namespace PipelineBuilder.Web.Storage;

/// <summary>
/// The team's database as the pages use it: whether there is one, who is signed in, and logging that never
/// stops what the user did. Made from the services, so a page without a database needs nothing registered.
/// </summary>
public sealed class Team(IServiceProvider services)
{
    /// <summary>The name logged when nobody is signed in, e.g. when the app runs locally.</summary>
    public const string Unknown = "unknown";

    public IPipelineStore? Store { get; } = services.GetService(typeof(IPipelineStore)) is IPipelineStore { IsAvailable: true } store ? store : null;

    public bool IsAvailable => Store != null;

    public async Task<string> UserNameAsync()
    {
        if (services.GetService(typeof(AuthenticationStateProvider)) is not AuthenticationStateProvider provider)
            return Unknown;
        var name = (await provider.GetAuthenticationStateAsync()).User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? Unknown : name;
    }

    /// <summary>Adds a line to the activity log. A database that cannot be reached is logged, not shown.</summary>
    public async Task LogAsync(string action, string pipeline)
    {
        if (Store == null)
            return;
        try
        {
            await Store.LogAsync(await UserNameAsync(), action, string.IsNullOrWhiteSpace(pipeline) ? "(no name)" : pipeline);
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            (services.GetService(typeof(ILogger<Team>)) as ILogger<Team>)?.LogWarning(ex, "Could not write {Action} to the activity log", action);
        }
    }

    /// <summary>The database could not be reached or refused the request.</summary>
    public static bool IsDatabaseError(Exception ex) => ex is DbException or TimeoutException or InvalidOperationException;
}
