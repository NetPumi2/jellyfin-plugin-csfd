using System;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// A cached ČSFD lookup result for a given title/year.
/// </summary>
/// <param name="FetchedAtUtc">When this entry was fetched.</param>
/// <param name="CsfdUrl">The resolved ČSFD detail page URL, if any.</param>
/// <param name="RatingPercent">The ČSFD rating (0-100), if any.</param>
public sealed record CsfdCacheEntry(DateTime FetchedAtUtc, string? CsfdUrl, int? RatingPercent);
