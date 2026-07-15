using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Net.Sockets;

namespace MolasLubes.Infrastructure.Persistence;

/// <summary>
/// Retry strategy for Neon (serverless Postgres). Extends the default EF Core strategy to
/// also retry on network-level resets (SocketException / IOException) that occur when Neon's
/// serverless compute closes the TCP connection mid-query. NpgsqlException.IsTransient only
/// checks PostgreSQL SqlState codes and returns false for bare socket errors, so without this
/// class those errors surface immediately with no retry.
/// </summary>
public sealed class NeonRetryStrategy : ExecutionStrategy
{
    public NeonRetryStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount, TimeSpan maxRetryDelay)
        : base(dependencies, maxRetryCount, maxRetryDelay)
    {
    }

    protected override bool ShouldRetryOn(Exception exception)
    {
        // Walk the full exception chain so inner exceptions are also caught.
        for (var ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is NpgsqlException npgsql && npgsql.IsTransient)
                return true;

            if (ex is IOException or SocketException)
                return true;
        }

        return false;
    }
}
