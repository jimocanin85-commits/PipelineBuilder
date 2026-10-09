using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Web.Storage;

/// <summary>
/// <see cref="IPipelineStore"/> in SQL Server, with the connection string <c>ConnectionStrings:PipelineBuilder</c>.
/// Every value goes in as a parameter. The tables are made by <c>Database/schema.sql</c>.
/// </summary>
public sealed class SqlPipelineStore(string connectionString) : IPipelineStore
{
    public const string ConnectionStringName = "PipelineBuilder";

    /// <summary>The longest name and user name the tables hold.</summary>
    public const int MaxNameLength = 200;
    private const int MaxUserLength = 256;

    public bool IsAvailable => true;

    /// <summary>The script that makes the tables, as shipped next to the app in <c>Database/schema.sql</c>.</summary>
    public static string Schema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PipelineBuilder.Web.Database.schema.sql")
            ?? throw new InvalidOperationException("Database/schema.sql is missing from the app.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Makes the tables that are missing. Needs the right to create tables; the install script does it.</summary>
    public async Task CreateSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(Schema(), connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPipeline>> ListAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT Id, Name, SavedBy, SavedAt FROM dbo.SavedPipelines ORDER BY Name";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<SavedPipeline>();
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new SavedPipeline(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDateTimeOffset(3)));
        return list;
    }

    public async Task<string?> LoadAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT Settings FROM dbo.SavedPipelines WHERE Id = @id", connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SaveAsync(string name, string settings, string userName, CancellationToken cancellationToken = default)
    {
        name = Checked(name, MaxNameLength, nameof(name));
        if (settings.Length > PipelineDefinitionSerializer.MaxFileSize)
            throw new ArgumentException("The settings are too large to save.", nameof(settings));

        // One statement, so two people saving the same name at once cannot both insert it.
        const string sql = """
            MERGE dbo.SavedPipelines WITH (HOLDLOCK) AS saved
            USING (SELECT @name AS Name) AS incoming ON saved.Name = incoming.Name
            WHEN MATCHED THEN UPDATE SET Name = @name, Settings = @settings, SavedBy = @user, SavedAt = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN INSERT (Name, Settings, SavedBy, SavedAt) VALUES (@name, @settings, @user, SYSDATETIMEOFFSET());
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@name", SqlDbType.NVarChar, MaxNameLength).Value = name;
        command.Parameters.Add("@settings", SqlDbType.NVarChar, -1).Value = settings;
        command.Parameters.Add("@user", SqlDbType.NVarChar, MaxUserLength).Value = Checked(userName, MaxUserLength, nameof(userName));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, string userName, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("DELETE FROM dbo.SavedPipelines WHERE Id = @id AND SavedBy = @user", connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        command.Parameters.Add("@user", SqlDbType.NVarChar, MaxUserLength).Value = userName;
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task LogAsync(string userName, string action, string pipeline, CancellationToken cancellationToken = default)
    {
        const string sql = "INSERT INTO dbo.ActivityLog (At, UserName, Action, Pipeline) VALUES (SYSDATETIMEOFFSET(), @user, @action, @pipeline)";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@user", SqlDbType.NVarChar, MaxUserLength).Value = Checked(userName, MaxUserLength, nameof(userName));
        command.Parameters.Add("@action", SqlDbType.NVarChar, 50).Value = action;
        command.Parameters.Add("@pipeline", SqlDbType.NVarChar, MaxNameLength).Value = Shortened(pipeline, MaxNameLength);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityEntry>> RecentActivityAsync(int count, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT TOP (@count) At, UserName, Action, Pipeline FROM dbo.ActivityLog ORDER BY At DESC, Id DESC";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@count", SqlDbType.Int).Value = count;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<ActivityEntry>();
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new ActivityEntry(reader.GetDateTimeOffset(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return list;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string Checked(string value, int maxLength, string parameter)
    {
        value = value.Trim();
        if (value.Length == 0)
            throw new ArgumentException("A value is needed.", parameter);
        if (value.Length > maxLength)
            throw new ArgumentException($"At most {maxLength} characters.", parameter);
        return value;
    }

    private static string Shortened(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
