using System;
using System.Linq;
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

    // CriticRating is a single shared field other metadata providers (e.g. an OMDb/Rotten
    // Tomatoes provider) also write to, and whichever provider runs last wins - so setting it
    // here got silently overwritten by the other provider's own rating. A Tag is additive and
    // not contested by other providers, and shows as plain readable text on the item's detail
    // page instead of reusing the tomato icon that's already spoken for by Rotten Tomatoes.
    private const string CsfdTagPrefix = "ČSFD: ";

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
            logger.LogInformation(
                "ČSFD: použita cache pro '{Name}' ({Year}) - hodnocení: {Rating}", name, year, cached.RatingPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "žádné");
            return ApplyResult(item, cached.RatingPercent, cached.CsfdUrl, name, logger);
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
                return ApplyResult(item, result.RatingPercent, result.CsfdUrl, name, logger);

            case CsfdLookupStatus.Unrated:
                cache.Set(name, year, new CsfdCacheEntry(DateTime.UtcNow, result.CsfdUrl, null));
                return ApplyResult(item, null, result.CsfdUrl, name, logger);

            case CsfdLookupStatus.NotFound:
                logger.LogInformation("ČSFD: Tag NEpřidán pro '{Name}' ({Year}), důvod: žádný výsledek vyhledávání.", name, year);
                return ItemUpdateType.None;

            case CsfdLookupStatus.AnubisBlocked:
                // CsfdClient already logged a clear "refresh the cookie" message; not cached
                // deliberately, so the very next refresh retries once the cookie is fixed.
                logger.LogInformation("ČSFD: Tag NEpřidán pro '{Name}' ({Year}), důvod: Anubis ochrana zablokovala request (viz warning výše).", name, year);
                return ItemUpdateType.None;

            case CsfdLookupStatus.Error:
                logger.LogInformation("ČSFD: Tag NEpřidán pro '{Name}' ({Year}), důvod: chyba při stahování (viz warning výše).", name, year);
                return ItemUpdateType.None;

            default:
                return ItemUpdateType.None;
        }
    }

    /// <summary>
    /// Whether this item is worth handing to <see cref="FetchAsync"/> during a non-full library
    /// scan: either we have no still-valid cached result for it yet (never looked up, or the
    /// cache entry expired), or we do have one but the item is missing the tag it should have
    /// (e.g. because something else replaced its tags).
    /// </summary>
    public static bool HasChanged(BaseItem item, ILoggerFactory loggerFactory)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return false;
        }

        var name = item.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var year = item.ProductionYear;
        var cache = CsfdServices.GetCache(loggerFactory);
        var ttl = TimeSpan.FromHours(Math.Max(1, config.CacheTtlHours));

        var cached = cache.TryGet(name, year, ttl);
        if (cached is null)
        {
            return true;
        }

        if (!cached.RatingPercent.HasValue)
        {
            return false;
        }

        var expectedTag = $"{CsfdTagPrefix}{cached.RatingPercent.Value}%";
        return !(item.Tags ?? Array.Empty<string>()).Contains(expectedTag, StringComparer.Ordinal);
    }

    private static ItemUpdateType ApplyResult(BaseItem item, int? ratingPercent, string? csfdUrl, string name, ILogger logger)
    {
        var updateType = ItemUpdateType.None;

        var existingTags = item.Tags ?? Array.Empty<string>();
        var withoutCsfdTag = existingTags
            .Where(tag => !tag.StartsWith(CsfdTagPrefix, StringComparison.Ordinal))
            .ToArray();

        var newTags = withoutCsfdTag;
        if (ratingPercent.HasValue)
        {
            newTags = new string[withoutCsfdTag.Length + 1];
            withoutCsfdTag.CopyTo(newTags, 0);
            newTags[^1] = $"{CsfdTagPrefix}{ratingPercent.Value}%";
        }

        if (!newTags.SequenceEqual(existingTags))
        {
            item.Tags = newTags;
            updateType |= ItemUpdateType.MetadataDownload;

            if (ratingPercent.HasValue)
            {
                logger.LogInformation("ČSFD: Tag přidán pro '{Name}': \"{Tag}\"", name, newTags[^1]);
            }
        }
        else if (!ratingPercent.HasValue)
        {
            logger.LogInformation("ČSFD: Tag NEpřidán pro '{Name}', důvod: film/seriál nalezen, ale nemá dost hodnocení (?).", name);
        }
        else
        {
            logger.LogInformation("ČSFD: Tag už byl aktuální pro '{Name}' (\"{Tag}\"), nic se nemění.", name, newTags[^1]);
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
