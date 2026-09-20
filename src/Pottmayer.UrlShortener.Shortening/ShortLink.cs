namespace Pottmayer.UrlShortener.Shortening;

/// <summary>A shortened link: the source of truth owned by the Shortening service.</summary>
public sealed class ShortLink
{
    public string Code { get; init; } = default!;
    public string LongUrl { get; init; } = default!;
    public DateTimeOffset CreatedAt { get; init; }
}
