using Pottmayer.Tars.Messaging.Abstractions;

namespace Pottmayer.UrlShortener.Contracts;

/// <summary>A short link was created. Produced by Shortening via the transactional outbox.</summary>
[IntegrationEventName("urlshortener.url-created.v1")]
public sealed record UrlCreated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string Code,
    string LongUrl) : IIntegrationEvent;

/// <summary>A short link was resolved (a click). Produced by Redirect best-effort; consumed by Analytics.</summary>
[IntegrationEventName("urlshortener.url-accessed.v1")]
public sealed record UrlAccessed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string Code) : IIntegrationEvent;
