using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>
/// Shared logic used by both the movie and series ČSFD providers: check the cache, fall back to
/// a live ČSFD lookup, and apply the resulting rating (and provider id) to the item.
/// </summary>
internal static class CsfdMetadataUpdater
{
    private const string CsfdProviderIdName = "Csfd";

    public static async Task<ItemUpdateType> FetchAsync(
        BaseItem item,
        CsfdItemKind kind,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return ItemUpdateType.None;
        }

        var name = item.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return ItemUpdateType.None;
        }

        var year = item.ProductionYear;
        var cache = CsfdServices.GetCache(loggerFactory);
        var ttl = TimeSpan.FromHours(Math.Max(1, config.CacheTtlHours));

        var cached = cache.TryGet(name, year, ttl);
        if (cached is not null)
        {
            return ApplyResult(item, cached.RatingPercent, cached.CsfdUrl);
        }

        var client = CsfdServices.GetClient(httpClientFactory, loggerFactory);
        CsfdLookupResult result;
        try
        {
            result = await client.LookupAsync(name, year, kind, config.CsfdSessionCookie, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ČSFD lookup selhal pro '{Name}' ({Year})", name, year);
            return ItemUpdateType.None;
        }

        switch (result.Status)
        {
            case CsfdLookupStatus.Found:
                cache.Set(name, year, new CsfdCacheEntry(DateTime.UtcNow, result.CsfdUrl, result.RatingPercent));
                return ApplyResult(item, result.RatingPercent, result.CsfdUrl);

            case CsfdLookupStatus.Unrated:
                cache.Set(name, year, new CsfdCacheEntry(DateTime.UtcNow, result.CsfdUrl, null));
                logger.LogDebug("ČSFD: '{Name}' ({Year}) zatím nemá dost hodnocení (?), rating nenastavuji.", name, year);
                return ItemUpdateType.None;

            case CsfdLookupStatus.NotFound:
                logger.LogDebug("ČSFD: nenalezen výsledek pro '{Name}' ({Year}).", name, year);
                return ItemUpdateType.None;

            case CsfdLookupStatus.AnubisBlocked:
                // CsfdClient already logged a clear "refresh the cookie" message; not cached
                // deliberately, so the very next refresh retries once the cookie is fixed.
                return ItemUpdateType.None;

            case CsfdLookupStatus.Error:
            default:
                return ItemUpdateType.None;
        }
    }

    private static ItemUpdateType ApplyResult(BaseItem item, int? ratingPercent, string? csfdUrl)
    {
        var updateType = ItemUpdateType.None;

        if (ratingPercent.HasValue)
        {
            var newValue = (float)ratingPercent.Value;
            if (item.CriticRating != newValue)
            {
                item.CriticRating = newValue;
                updateType |= ItemUpdateType.MetadataDownload;
            }
        }

        if (!string.IsNullOrEmpty(csfdUrl)
            && (!item.ProviderIds.TryGetValue(CsfdProviderIdName, out var existing) || existing != csfdUrl))
        {
            item.SetProviderId(CsfdProviderIdName, csfdUrl);
            updateType |= ItemUpdateType.MetadataDownload;
        }

        return updateType;
    }
}
