using System.Linq.Expressions;
using Pottmayer.Tars.Data.Abstractions.DataContext;
using Pottmayer.Tars.Data.Abstractions.Repositories;
using Pottmayer.Tars.Data.Document.MongoDB.Repositories;

namespace Pottmayer.UrlShortener.Analytics;

public interface IAccessEventRepository : IStandardRepository<AccessEvent, string>;

public sealed class AccessEventRepository(IDataContextAccessor accessor)
    : MongoStandardRepository<AccessEvent, string>(accessor), IAccessEventRepository
{
    protected override string CollectionName => "access_event";
    protected override Expression<Func<AccessEvent, string>> IdSelector => e => e.Id;
}
