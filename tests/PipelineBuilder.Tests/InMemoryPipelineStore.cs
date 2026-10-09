using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PipelineBuilder.Web.Storage;

namespace PipelineBuilder.Tests;

/// <summary>The team's database, in memory, for the page tests. <see cref="Broken"/> makes every call fail as a lost database does.</summary>
public sealed class InMemoryPipelineStore : IPipelineStore
{
    private readonly List<(int Id, string Name, string Settings, string SavedBy, DateTimeOffset SavedAt)> _saved = new();
    private int _nextId = 1;

    public List<ActivityEntry> Log { get; } = new();

    public bool Broken { get; set; }

    public bool IsAvailable => true;

    public IReadOnlyList<(int Id, string Name, string Settings, string SavedBy)> Saved =>
        _saved.Select(s => (s.Id, s.Name, s.Settings, s.SavedBy)).ToList();

    /// <summary>Puts a pipeline in as someone else saved it.</summary>
    public int Add(string name, string settings, string savedBy)
    {
        _saved.Add((_nextId, name, settings, savedBy, DateTimeOffset.Now));
        return _nextId++;
    }

    public Task<IReadOnlyList<SavedPipeline>> ListAsync(CancellationToken cancellationToken = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<SavedPipeline>>(_saved.OrderBy(s => s.Name).Select(s => new SavedPipeline(s.Id, s.Name, s.SavedBy, s.SavedAt)).ToList());
    }

    public Task<string?> LoadAsync(int id, CancellationToken cancellationToken = default)
    {
        Check();
        return Task.FromResult(_saved.Where(s => s.Id == id).Select(s => s.Settings).FirstOrDefault());
    }

    public Task SaveAsync(string name, string settings, string userName, CancellationToken cancellationToken = default)
    {
        Check();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A value is needed.", nameof(name));
        _saved.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        Add(name, settings, userName);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(int id, string userName, CancellationToken cancellationToken = default)
    {
        Check();
        return Task.FromResult(_saved.RemoveAll(s => s.Id == id && s.SavedBy == userName) > 0);
    }

    /// <summary>Takes a pipeline away, as when someone else deletes it while it is listed.</summary>
    public void Remove(int id) => _saved.RemoveAll(s => s.Id == id);

    public Task LogAsync(string userName, string action, string pipeline, CancellationToken cancellationToken = default)
    {
        Check();
        Log.Insert(0, new ActivityEntry(DateTimeOffset.Now, userName, action, pipeline));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityEntry>> RecentActivityAsync(int count, CancellationToken cancellationToken = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<ActivityEntry>>(Log.Take(count).ToList());
    }

    private void Check()
    {
        if (Broken)
            throw new InvalidOperationException("The database is not there.");
    }
}

/// <summary>A signed-in Windows user, as IIS gives the app.</summary>
public sealed class SignedIn(string name) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, name) }, "Negotiate"))));
}
