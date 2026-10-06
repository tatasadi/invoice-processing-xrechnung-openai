using Dapper;
using InvoiceProcessing.Core.Reading;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InvoiceProcessing.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    /// <summary>Create the tables if they do not exist. Set to false when the tables are provided by an existing database.</summary>
    public bool CreateSchemaIfMissing { get; set; } = true;
}

/// <summary>Checks the connection at startup and creates the tables if configured.</summary>
public sealed class DatabaseInitializer(NpgsqlDataSource dataSource, IOptions<DatabaseOptions> options)
{
    /// <returns>Short description of the connected database, e.g. "rechnungen auf localhost (PostgreSQL 17.6)".</returns>
    public async Task<string> InitializeAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            if (options.Value.CreateSchemaIfMissing)
                await connection.ExecuteAsync(new CommandDefinition(LoadSchemaScript(), cancellationToken: ct));

            return $"{connection.Database} auf {connection.Host} (PostgreSQL {connection.PostgreSqlVersion})";
        }
        catch (NpgsqlException ex)
        {
            throw new TransientProcessingException($"Datenbank nicht erreichbar: {ex.Message}", ex);
        }
    }

    private static string LoadSchemaScript()
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("schema.sql", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}
