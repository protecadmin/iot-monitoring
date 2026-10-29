using System.Net.Sockets;
using Bms.Application.Abstractions.Persistence;
using InfluxDB.Client.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL (Npgsql/EF Core) and InfluxDB failures that mean "the store
/// is unreachable or overloaded right now", as opposed to "this data can't
/// be stored". Walks inner exceptions, since EF Core and the Influx client
/// both wrap the underlying socket/timeout error.
/// </summary>
public sealed class TransientFailureClassifier : ITransientFailureClassifier
{
    public bool IsTransient(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case OperationCanceledException:
                case TimeoutException:
                case SocketException:
                case IOException:
                case HttpRequestException:
                case RetryLimitExceededException:
                    return true;
                case PostgresException pg:
                    // Constraint/data errors are about the row; connection,
                    // resource, and shutdown classes are about the server.
                    return pg.IsTransient
                        || pg.SqlState.StartsWith("08", StringComparison.Ordinal)   // connection exception
                        || pg.SqlState.StartsWith("53", StringComparison.Ordinal)   // insufficient resources
                        || pg.SqlState.StartsWith("57", StringComparison.Ordinal);  // operator intervention (shutdown, cancel)
                case NpgsqlException npgsql when npgsql.IsTransient:
                    return true;
                case InfluxException influx when influx.Status is 0 or 408 or 429 or >= 500:
                    return true;
            }
        }

        return false;
    }
}
