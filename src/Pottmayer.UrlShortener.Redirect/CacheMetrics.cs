namespace Pottmayer.UrlShortener.Redirect;

/// <summary>
/// In-process hit/miss counters for the redirect cache. Hit = served from Redis (positive or
/// negative), miss = had to read Postgres. A lightweight stand-in until OTel lands in Fase 5.
/// </summary>
public sealed class CacheMetrics
{
    private long _hits;
    private long _misses;

    public void Hit() => Interlocked.Increment(ref _hits);
    public void Miss() => Interlocked.Increment(ref _misses);

    public (long Hits, long Misses) Snapshot() =>
        (Interlocked.Read(ref _hits), Interlocked.Read(ref _misses));
}
