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

    private static readonly Regex YearRegex = new(@"\d{4}", RegexOptions.Compiled);

    /// <summary>
    /// Finds the relative (or absolute) link to a result in the "Filmy" or "Seriály" section of
    /// a ČSFD search results page (<c>https://www.csfd.cz/hledat/?q=...</c>). ČSFD often lists
    /// several unrelated titles that share a name (remakes, unrelated foreign films, etc.), so
    /// when <paramref name="year"/> is known, this prefers the first result whose year matches
    /// it exactly. If no result matches the year (or the year isn't known), it falls back to the
    /// first result in the section, same as before year-awareness was added.
    /// </summary>
    /// <param name="searchResultsHtml">The raw HTML of the search results page.</param>
    /// <param name="kind">Whether to look under the movie or series result section.</param>
    /// <param name="year">The item's production year, if known, to disambiguate same-titled results.</param>
    /// <returns>The href of the best matching result, or <see langword="null"/> if the section has no results at all.</returns>
    public static string? FindFirstResultUrl(string searchResultsHtml, CsfdItemKind kind, int? year = null)
    {
        ArgumentNullException.ThrowIfNull(searchResultsHtml);

        var sectionClass = kind == CsfdItemKind.Movie ? "main-movies" : "main-series";

        var doc = new HtmlDocument();
        doc.LoadHtml(searchResultsHtml);

        var section = doc.DocumentNode.SelectSingleNode(
            $"//section[contains(concat(' ', normalize-space(@class), ' '), ' {sectionClass} ')]");

        var titleHeadings = section?.SelectNodes(
            ".//h3[contains(concat(' ', normalize-space(@class), ' '), ' film-title-nooverflow ')]");

        if (titleHeadings is null || titleHeadings.Count == 0)
        {
            return null;
        }

        if (year.HasValue)
        {
            foreach (var heading in titleHeadings)
            {
                var yearNode = heading.SelectSingleNode(
                    ".//span[contains(concat(' ', normalize-space(@class), ' '), ' film-title-info ')]"
                    + "/span[contains(concat(' ', normalize-space(@class), ' '), ' info ')][1]");

                var yearMatch = yearNode is not null ? YearRegex.Match(yearNode.InnerText) : null;
                if (yearMatch is { Success: true } && int.Parse(yearMatch.Value, CultureInfo.InvariantCulture) == year.Value)
                {
                    var matchHref = GetResultHref(heading);
                    if (matchHref is not null)
                    {
                        return matchHref;
                    }
                }
            }
        }

        // No year given, or none of the results matched it - fall back to the first result.
        return GetResultHref(titleHeadings[0]);
    }

    private static string? GetResultHref(HtmlNode titleHeading)
    {
        var link = titleHeading.SelectSingleNode(
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
