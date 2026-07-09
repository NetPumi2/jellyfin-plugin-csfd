using System;
using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Pure HTML parsing helpers for ČSFD search result pages and film/series detail pages.
/// Kept free of any I/O so it can be exercised directly by unit tests against saved HTML fixtures.
/// </summary>
public static class CsfdHtmlParser
{
    private static readonly Regex RatingRegex = new(@"(\d+|\?)\s*%", RegexOptions.Compiled);

    /// <summary>
    /// Finds the relative (or absolute) link to the first result in the "Filmy" or "Seriály"
    /// section of a ČSFD search results page (<c>https://www.csfd.cz/hledat/?q=...</c>).
    /// </summary>
    /// <param name="searchResultsHtml">The raw HTML of the search results page.</param>
    /// <param name="kind">Whether to look under the movie or series result section.</param>
    /// <returns>The href of the first matching result, or <see langword="null"/> if the section or a result was not found.</returns>
    public static string? FindFirstResultUrl(string searchResultsHtml, CsfdItemKind kind)
    {
        ArgumentNullException.ThrowIfNull(searchResultsHtml);

        var sectionClass = kind == CsfdItemKind.Movie ? "main-movies" : "main-series";

        var doc = new HtmlDocument();
        doc.LoadHtml(searchResultsHtml);

        var section = doc.DocumentNode.SelectSingleNode(
            $"//section[contains(concat(' ', normalize-space(@class), ' '), ' {sectionClass} ')]");

        var link = section?.SelectSingleNode(
            ".//a[contains(concat(' ', normalize-space(@class), ' '), ' film-title-name ')]");

        var href = link?.GetAttributeValue("href", string.Empty);
        return string.IsNullOrWhiteSpace(href) ? null : href;
    }

    /// <summary>
    /// Extracts the ČSFD percentage rating from a film/series detail page, e.g. from
    /// <c>&lt;div class="film-rating-average"&gt;63%&lt;/div&gt;</c>.
    /// </summary>
    /// <param name="detailPageHtml">The raw HTML of the film/series detail page.</param>
    /// <returns>
    /// The rating (0-100), or <see langword="null"/> if the element is missing or shows "?"
    /// (ČSFD's placeholder for a title that does not have enough ratings yet).
    /// </returns>
    public static int? ExtractRatingPercent(string detailPageHtml)
    {
        ArgumentNullException.ThrowIfNull(detailPageHtml);

        var doc = new HtmlDocument();
        doc.LoadHtml(detailPageHtml);

        var node = doc.DocumentNode.SelectSingleNode(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' film-rating-average ')]");

        if (node is null)
        {
            return null;
        }

        var match = RatingRegex.Match(node.InnerText);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value;
        if (value == "?")
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating)
            ? rating
            : null;
    }

    /// <summary>
    /// Detects whether a response is ČSFD's Anubis anti-bot proof-of-work challenge page rather
    /// than real content - this happens when the configured session cookie is missing, expired,
    /// or otherwise invalid.
    /// </summary>
    /// <param name="html">The raw HTML of the response.</param>
    /// <returns><see langword="true"/> if the response looks like an Anubis challenge page.</returns>
    public static bool IsAnubisChallenge(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return html.Contains("id=\"anubis_challenge\"", StringComparison.Ordinal)
            || html.Contains("Making sure you're not a bot", StringComparison.Ordinal);
    }
}
