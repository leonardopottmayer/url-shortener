namespace Pottmayer.UrlShortener.Kgs;

/// <summary>The single global counter row. Ranges are allocated by advancing <see cref="Value"/>.</summary>
public sealed class KeyCounter
{
    public int Id { get; init; }
    public long Value { get; init; }
}
