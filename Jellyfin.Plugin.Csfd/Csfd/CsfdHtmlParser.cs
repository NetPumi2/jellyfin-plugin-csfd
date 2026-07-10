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

    private static readonly Regex YearInParensRegex = new(@"\((\d{4})\)", RegexOptions.Compiled);

    // Matches hrefs like "/film/1328563-superman/" or "/film/86620-oppenheimer/prehled/" - ČSFD
    // uses this same "/film/{numeric id}-{slug}/" path for both movies and series. Anchoring on
    // the href shape (rather than a specific inner CSS class, which churns more) is deliberately
    // more resilient to markup tweaks on ČSFD's side.
    private static readonly Regex FilmHrefRegex = new(@"^/film/\d+-", RegexOptions.Compiled);

    /// <summary>
    /// Finds the relative (or absolute) link to a result in the "Filmy" or "Seriály" section of
    /// a ČSFD search results page (<c>https://www.csfd.cz/hledat/?q=...</c>). Each result is an
    /// <c>&lt;article&gt;</c> element whose class contains <c>article-poster</c> (e.g.
    /// <c>article-poster-50</c>). ČSFD often lists several unrelated titles that share a name
    /// (remakes, unrelated foreign films, etc.), so when <paramref name="year"/> is known, this
    /// prefers the first result whose year (rendered in parentheses next to the title) matches it
    /// exactly. If no result matches the year (or the year isn't known), it falls back to the
    /// first result in the section.
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

        var articles = section?.SelectNodes(".//article[contains(@class, 'article-poster')]");

        if (articles is null || articles.Count == 0)
        {
            return null;
        }

        if (year.HasValue)
        {
            foreach (var article in articles)
            {
                var yearMatch = YearInParensRegex.Match(article.InnerText);
                if (yearMatch.Success && int.Parse(yearMatch.Groups[1].Value, CultureInfo.InvariantCulture) == year.Value)
                {
                    var matchHref = GetFilmHref(article);
                    if (matchHref is not null)
                    {
                        return matchHref;
                    }
                }
            }
        }

        // No year given, or none of the results matched it - fall back to the first result.
        return GetFilmHref(articles[0]);
    }

    private static string? GetFilmHref(HtmlNode article)
    {
        var links = article.SelectNodes(".//a[@href]");
        if (links is null)
        {
            return null;
        }

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", string.Empty);
            if (FilmHrefRegex.IsMatch(href))
            {
                return href;
            }
        }

        return null;
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
