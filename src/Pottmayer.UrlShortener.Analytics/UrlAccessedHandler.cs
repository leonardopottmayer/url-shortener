using Pottmayer.Tars.Data.Abstractions.UnitOfWork;
using Pottmayer.Tars.Messaging.Abstractions;
using Pottmayer.UrlShortener.Contracts;

namespace Pottmayer.UrlShortener.Analytics;

/// <summary>Stores each click in Mongo. Idempotent on the event id (at-least-once delivery).</summary>
public sealed class UrlAccessedHandler(IUnitOfWorkFactory uow) : IIntegrationEventHandler<UrlAccessed>
{
    public async Task HandleAsync(UrlAccessed @event, CancellationToken cancellationToken = default)
    {
        await uow.ExecuteAsync(async (ctx, token) =>
        {
            var repo = ctx.AcquireRepository<IAccessEventRepository>();

            var id = @event.EventId.ToString();
            if (await repo.ExistsKeyAsync(id, token))
                return;

            await repo.AddAsync(new AccessEvent
            {
                Id = id,
                Code = @event.Code,
                OccurredAt = @event.OccurredAt,
            }, token);
        }, cancellationToken: cancellationToken);
    }
}
