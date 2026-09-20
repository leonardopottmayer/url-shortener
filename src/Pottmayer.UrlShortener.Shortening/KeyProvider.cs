namespace Pottmayer.UrlShortener.Shortening;

/// <summary>
/// Hands out counter values from a locally buffered KGS range, refilling from the KGS when the
/// buffer is exhausted. In-memory only (Fase 2): on restart the unused tail of the range is lost —
/// that leaves gaps in the sequence, which is fine because uniqueness comes from the KGS counter
/// only ever moving forward. Persisting the cursor (Redis) is a Fase 3 concern.
/// </summary>
public sealed class KeyProvider(IKgsClient kgs)
{
    private const int RangeSize = 1_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _next = 1;
    private long _end;      // buffer is empty while _next > _end

    public async Task<long> NextAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_next > _end)
            {
                var (start, end) = await kgs.AllocateAsync(RangeSize, ct);
                _next = start;
                _end = end;
            }

            return _next++;
        }
        finally
        {
            _gate.Release();
        }
    }
}
