using Pottmayer.Tars.Caching.Abstractions;

namespace Pottmayer.UrlShortener.Shortening;

/// <summary>
/// Hands out counter values from a locally buffered KGS range, refilling from the KGS when the
/// buffer is exhausted. The cursor (next/end) is persisted to Redis after each hand-out and reloaded
/// on startup, so a restart resumes the same range instead of discarding its unused tail — no gaps.
/// </summary>
public sealed class KeyProvider(IKgsClient kgs, ICacheStore cache)
{
    private const int RangeSize = 1_000;
    private const string CursorKey = "kgs-cursor";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    private long _next = 1;
    private long _end;      // buffer is empty while _next > _end

    public async Task<long> NextAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loaded)
            {
                var saved = await cache.TryGetAsync<KgsCursor>(CursorKey, ct);
                if (saved is { Found: true, Value: { } cursor } && cursor.Next <= cursor.End)
                {
                    _next = cursor.Next;
                    _end = cursor.End;
                }
                _loaded = true;
            }

            if (_next > _end)
            {
                var (start, end) = await kgs.AllocateAsync(RangeSize, ct);
                _next = start;
                _end = end;
            }

            var value = _next++;
            await cache.SetAsync(CursorKey, new KgsCursor(_next, _end), ct: ct); // no TTL: the cursor must persist
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record KgsCursor(long Next, long End);
}
