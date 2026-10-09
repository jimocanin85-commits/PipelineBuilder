using Microsoft.Data.SqlClient;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Web.Storage;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>
/// The store against a real SQL Server, in a database made as the install script makes it (Danish_Norwegian_CI_AS).
/// Runs when PIPELINEBUILDER_SQL holds a connection string to a server where databases may be made (CI starts one).
/// </summary>
public sealed class SqlPipelineStoreTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("PIPELINEBUILDER_SQL");
    private readonly string _database = $"PipelineBuild.Tests.{Guid.NewGuid():N}";
    private SqlPipelineStore _store = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(Server))
            return;
        await Execute(Server, $"CREATE DATABASE [{_database}] COLLATE Danish_Norwegian_CI_AS");
        _store = new SqlPipelineStore(new SqlConnectionStringBuilder(Server) { InitialCatalog = _database }.ConnectionString);
        await _store.CreateSchemaAsync();
        await _store.CreateSchemaAsync(); // runs again on every install
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(Server))
            return;
        SqlConnection.ClearAllPools();
        await Execute(Server, $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
    }

    private static async Task<object?> Execute(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    [SkippableFact]
    public async Task PipelinesAreSavedListedOpenedAndReplaced()
    {
        Skip.If(string.IsNullOrEmpty(Server), "PIPELINEBUILDER_SQL is not set.");

        await _store.SaveAsync("orders-api", "{\"v\":1}", @"CONTOSO\anna");
        await _store.SaveAsync("billing", "{\"v\":2}", @"CONTOSO\bob");
        await _store.SaveAsync(" orders-api ", "{\"v\":3}", @"CONTOSO\bob"); // the same name replaces it

        var list = await _store.ListAsync();
        Assert.Equal(new[] { "billing", "orders-api" }, list.Select(p => p.Name));
        var orders = list[1];
        Assert.Equal(@"CONTOSO\bob", orders.SavedBy);
        Assert.Equal("{\"v\":3}", await _store.LoadAsync(orders.Id));
        Assert.Null(await _store.LoadAsync(-1));
    }

    [SkippableFact]
    public async Task NamesFollowTheDanishCollation()
    {
        Skip.If(string.IsNullOrEmpty(Server), "PIPELINEBUILDER_SQL is not set.");

        Assert.Equal("Danish_Norwegian_CI_AS", await Execute(Server!, $"SELECT CAST(DATABASEPROPERTYEX(N'{_database}', 'Collation') AS nvarchar(128))"));
        await _store.SaveAsync("Æblegrød", "{}", "a");
        await _store.SaveAsync("æblegrød", "{}", "b"); // case does not matter, as in the rest of the database
        await _store.SaveAsync("Aablegrød", "{}", "c");

        // Danish order: Æ before Aa, which is sorted as Å, last in the alphabet.
        Assert.Equal(new[] { "æblegrød", "Aablegrød" }, (await _store.ListAsync()).Select(p => p.Name));
    }

    [SkippableFact]
    public async Task OnlyTheOneWhoSavedAPipelineCanDeleteIt()
    {
        Skip.If(string.IsNullOrEmpty(Server), "PIPELINEBUILDER_SQL is not set.");

        await _store.SaveAsync("orders-api", "{}", @"CONTOSO\anna");
        var id = Assert.Single(await _store.ListAsync()).Id;

        Assert.False(await _store.DeleteAsync(id, @"CONTOSO\bob"));
        Assert.True(await _store.DeleteAsync(id, @"CONTOSO\anna"));
        Assert.Empty(await _store.ListAsync());
    }

    [SkippableFact]
    public async Task TheActivityLogKeepsTheNewestFirst()
    {
        Skip.If(string.IsNullOrEmpty(Server), "PIPELINEBUILDER_SQL is not set.");

        await _store.LogAsync(@"CONTOSO\anna", ActivityKind.Saved, "orders-api");
        await _store.LogAsync(@"CONTOSO\bob", ActivityKind.Downloaded, new string('x', 300)); // a long name is shortened

        var log = await _store.RecentActivityAsync(10);
        Assert.Equal(new[] { ActivityKind.Downloaded, ActivityKind.Saved }, log.Select(l => l.Action));
        Assert.Equal(SqlPipelineStore.MaxNameLength, log[0].Pipeline.Length);
        Assert.Single(await _store.RecentActivityAsync(1));
    }

    [SkippableFact]
    public async Task NamesAndSettingsAreChecked()
    {
        Skip.If(string.IsNullOrEmpty(Server), "PIPELINEBUILDER_SQL is not set.");

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(" ", "{}", "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(new string('n', 201), "{}", "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync("a", new string(' ', PipelineDefinitionSerializer.MaxFileSize + 1), "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync("a", "{}", ""));
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public void TheSchemaShipsWithTheApp()
    {
        var schema = SqlPipelineStore.Schema();
        Assert.Contains("CREATE TABLE dbo.SavedPipelines", schema);
        Assert.Contains("CREATE TABLE dbo.ActivityLog", schema);
        // The install script runs the copy next to the app; it is the same file.
        Assert.Equal(schema.ReplaceLineEndings(), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "schema.sql")).ReplaceLineEndings());
    }

    [Fact]
    public async Task AServerThatCannotBeReachedFailsAsADatabaseError()
    {
        var store = new SqlPipelineStore("Server=tcp:127.0.0.1,1;Database=x;Integrated Security=False;User ID=x;Password=x;Connect Timeout=1;Encrypt=True");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => store.ListAsync());
        Assert.True(Team.IsDatabaseError(error), error.GetType().FullName);
    }
}
