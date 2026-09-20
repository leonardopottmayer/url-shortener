namespace Pottmayer.UrlShortener.Redirect;

/// <summary>
/// Read-only view of the short_link table (owned by Shortening). The Redirect service reads it
/// directly from Postgres on a cache miss — see design.md §7. Its own minimal mapping keeps the
/// service self-contained.
/// </summary>
public sealed class ShortLink
{
    public string Code { get; init; } = default!;
    public string LongUrl { get; init; } = default!;
}
