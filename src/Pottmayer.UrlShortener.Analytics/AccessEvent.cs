namespace Pottmayer.UrlShortener.Analytics;

/// <summary>A stored click. <see cref="Id"/> is the event id, so a redelivery is idempotent.</summary>
public sealed class AccessEvent
{
    public string Id { get; init; } = default!;
    public string Code { get; init; } = default!;
    public DateTimeOffset OccurredAt { get; init; }
}
