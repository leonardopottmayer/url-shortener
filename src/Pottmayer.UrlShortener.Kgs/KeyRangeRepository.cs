using Dapper;
using Pottmayer.Tars.Data.Abstractions.DataContext;
using Pottmayer.Tars.Data.Abstractions.Repositories;
using Pottmayer.Tars.Data.Relational.Repositories;

namespace Pottmayer.UrlShortener.Kgs;

public interface IKeyRangeRepository : IStandardRepository<KeyCounter, int>
{
    /// <summary>Atomically advances the counter by <paramref name="size"/> and returns the new high value.</summary>
    Task<long> AllocateAsync(int size, CancellationToken ct);
}

public sealed class KeyRangeRepository(IDataContextAccessor accessor)
    : StandardRepository<KeyCounter, int>(accessor), IKeyRangeRepository
{
    // A single UPDATE ... RETURNING is atomic on its own (row lock + autocommit): no read-modify-write
    // race even under concurrent allocations.
    public Task<long> AllocateAsync(int size, CancellationToken ct) =>
        Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "UPDATE key_counter SET value = value + @size WHERE id = 1 RETURNING value",
            new { size },
            cancellationToken: ct));
}
