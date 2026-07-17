using Dapper;
using MySqlConnector;

namespace ToBeClarify.Api.Infrastructure;

public sealed class AppDbContext
{
    private readonly string _connectionString;

    public AppDbContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public MySqlConnection CreateConnection() => new(_connectionString);

    public async Task<MySqlConnection> CreateOpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "SET time_zone = '+08:00';",
                cancellationToken: cancellationToken));
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
