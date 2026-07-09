using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>
/// Adds a ČSFD percentage rating (as a <c>"ČSFD: NN%"</c> tag on
/// <see cref="MediaBrowser.Controller.Entities.BaseItem.Tags"/>) to series during a library
/// metadata refresh.
/// </summary>
public class CsfdSeriesProvider : ICustomMetadataProvider<Series>, IHasOrder, IHasItemChangeMonitor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CsfdSeriesProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdSeriesProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    public CsfdSeriesProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CsfdSeriesProvider>();
    }

    /// <inheritdoc />
    public string Name => "ČSFD Rating";

    /// <summary>
    /// Gets the order this provider runs in, relative to other custom metadata providers. Runs
    /// after the built-in providers so it only adds a rating rather than racing anything.
    /// </summary>
    public int Order => 100;

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(Series item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return CsfdMetadataUpdater.FetchAsync(item, CsfdItemKind.Series, _httpClientFactory, _loggerFactory, _logger, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Without this, a plain library scan of already-imported items never runs this provider at
    /// all - Jellyfin only re-runs custom providers for existing items on a non-full refresh when
    /// one of them reports a change here, otherwise it would require a "Replace all metadata"
    /// refresh every time.
    /// </remarks>
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        return CsfdMetadataUpdater.HasChanged(item, _loggerFactory);
    }
}
