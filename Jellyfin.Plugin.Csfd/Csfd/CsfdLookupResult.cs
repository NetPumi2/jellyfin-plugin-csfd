namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Outcome of a <see cref="CsfdClient"/> lookup.
/// </summary>
public enum CsfdLookupStatus
{
    /// <summary>
    /// A result was found and it has a numeric rating.
    /// </summary>
    Found,

    /// <summary>
    /// A result was found but ČSFD does not have enough ratings yet (shows "?").
    /// </summary>
    Unrated,

    /// <summary>
    /// No matching result was found in the search results.
    /// </summary>
    NotFound,

    /// <summary>
    /// ČSFD's Anubis anti-bot challenge blocked the request (missing/expired/invalid session cookie).
    /// </summary>
    AnubisBlocked,

    /// <summary>
    /// The request failed for another reason (network error, unexpected HTTP status, etc.).
    /// </summary>
    Error
}

/// <summary>
/// Result of looking up a title on ČSFD.
/// </summary>
/// <param name="Status">The outcome of the lookup.</param>
/// <param name="CsfdUrl">The resolved ČSFD detail page URL, if a result was found.</param>
/// <param name="RatingPercent">The ČSFD rating (0-100), if available.</param>
public sealed record CsfdLookupResult(CsfdLookupStatus Status, string? CsfdUrl, int? RatingPercent);
