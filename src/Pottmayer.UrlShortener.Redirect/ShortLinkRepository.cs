using Pottmayer.Tars.Data.Abstractions.DataContext;
using Pottmayer.Tars.Data.Abstractions.Repositories;
using Pottmayer.Tars.Data.Relational.Repositories;

namespace Pottmayer.UrlShortener.Redirect;

public interface IShortLinkRepository : IStandardRepository<ShortLink, string>;

public sealed class ShortLinkRepository(IDataContextAccessor accessor)
    : StandardRepository<ShortLink, string>(accessor), IShortLinkRepository;
