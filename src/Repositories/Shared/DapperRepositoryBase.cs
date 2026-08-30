using Dapper;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Repositories.Shared;

// Database account policy: the runtime account is SELECT/INSERT/UPDATE only.
// Do not add DELETE, DROP, TRUNCATE, or equivalent destructive SQL to repositories.
// Use status/soft-disable updates and audit history so order and reporting data remain recoverable.
public abstract class DapperRepositoryBase
{
    protected DapperRepositoryBase(AppDbContext dbContext)
    {
        DbContext = dbContext;
    }

    protected AppDbContext DbContext { get; }

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    protected async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }
}
