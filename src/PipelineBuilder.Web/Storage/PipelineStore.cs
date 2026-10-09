namespace PipelineBuilder.Web.Storage;

/// <summary>A pipeline the team saved, without its settings.</summary>
public sealed record SavedPipeline(int Id, string Name, string SavedBy, DateTimeOffset SavedAt);

/// <summary>One line of the activity log: who did what with which pipeline, and when.</summary>
public sealed record ActivityEntry(DateTimeOffset At, string UserName, string Action, string Pipeline);

/// <summary>What is logged.</summary>
public static class ActivityKind
{
    public const string Saved = "Saved";
    public const string Opened = "Opened";
    public const string Deleted = "Deleted";
    public const string Downloaded = "Downloaded";
    public const string Copied = "Copied";
}

/// <summary>
/// The team's saved pipelines and the activity log, kept in SQL Server. When no database is set up,
/// <see cref="IsAvailable"/> is false and the app works as before, from the browser and files.
/// </summary>
public interface IPipelineStore
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<SavedPipeline>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The settings JSON of a saved pipeline, or null when it is gone.</summary>
    Task<string?> LoadAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Saves under <paramref name="name"/>, replacing a pipeline saved under the same name.</summary>
    Task SaveAsync(string name, string settings, string userName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a pipeline, only when <paramref name="userName"/> saved it. False otherwise.</summary>
    Task<bool> DeleteAsync(int id, string userName, CancellationToken cancellationToken = default);

    Task LogAsync(string userName, string action, string pipeline, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityEntry>> RecentActivityAsync(int count, CancellationToken cancellationToken = default);
}

/// <summary>No database: nothing is offered, so nothing is called.</summary>
public sealed class NoPipelineStore : IPipelineStore
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<SavedPipeline>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SavedPipeline>>(Array.Empty<SavedPipeline>());

    public Task<string?> LoadAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    public Task SaveAsync(string name, string settings, string userName, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> DeleteAsync(int id, string userName, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task LogAsync(string userName, string action, string pipeline, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ActivityEntry>> RecentActivityAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityEntry>>(Array.Empty<ActivityEntry>());
}
