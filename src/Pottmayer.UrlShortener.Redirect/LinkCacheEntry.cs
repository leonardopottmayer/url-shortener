namespace Pottmayer.UrlShortener.Redirect;

/// <summary>
/// Cached resolution of a code. A null <see cref="LongUrl"/> is a negative entry (known-missing),
/// cached with a short TTL to stop unknown codes from hammering Postgres (cache penetration).
/// </summary>
public sealed record LinkCacheEntry(string? LongUrl);
