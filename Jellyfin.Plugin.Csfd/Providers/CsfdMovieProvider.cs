using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>
/// Adds a ČSFD percentage rating (as a <c>"ČSFD: NN%"</c> tag on
/// <see cref="MediaBrowser.Controller.Entities.BaseItem.Tags"/>) to movies during a library
/// metadata refresh.
/// </summary>
public class CsfdMovieProvider : ICustomMetadataProvider<Movie>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CsfdMovieProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdMovieProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    public CsfdMovieProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CsfdMovieProvider>();
    }

    /// <inheritdoc />
    public string Name => "ČSFD Rating";

    /// <summary>
    /// Gets the order this provider runs in, relative to other custom metadata providers. Runs
    /// after the built-in providers so it only adds a rating rather than racing anything.
    /// </summary>
    public int Order => 100;

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return CsfdMetadataUpdater.FetchAsync(item, CsfdItemKind.Movie, _httpClientFactory, _loggerFactory, _logger, cancellationToken);
    }
}
