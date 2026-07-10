using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Searches ČSFD for a movie/series by name and year, and scrapes its percentage rating.
/// </summary>
public class CsfdClient
{
    private const string BaseUrl = "https://www.csfd.cz";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CsfdClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{CsfdClient}"/> interface.</param>
    public CsfdClient(IHttpClientFactory httpClientFactory, ILogger<CsfdClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Builds the ČSFD search URL for a given title/year, e.g.
    /// <c>https://www.csfd.cz/hledat/?q=Superman+2025</c>.
    /// </summary>
    /// <param name="name">The title to search for.</param>
    /// <param name="year">The production year, if known.</param>
    /// <returns>The full search URL.</returns>
    public static string BuildSearchUrl(string name, int? year)
    {
        ArgumentNullException.ThrowIfNull(name);

        var query = year.HasValue ? $"{name} {year.Value}" : name;

        // Uri.EscapeDataString percent-encodes spaces as %20; ČSFD's own search form encodes
        // them as "+" (the conventional application/x-www-form-urlencoded style), so normalize
        // to that for a request that looks like it came from the real search box.
        var encoded = Uri.EscapeDataString(query).Replace("%20", "+", StringComparison.Ordinal);
        return $"{BaseUrl}/hledat/?q={encoded}";
    }

    /// <summary>
    /// Looks up a title on ČSFD and returns its rating, if found.
    /// </summary>
    /// <param name="name">The title to search for.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="kind">Whether this is a movie or a series.</param>
    /// <param name="sessionCookie">
    /// The raw <c>Cookie</c> header value captured from a real browser session on csfd.cz, used
    /// to get past ČSFD's Anubis anti-bot proof-of-work challenge. May be null/empty, in which
    /// case the request will almost certainly be blocked.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The lookup result.</returns>
    public async Task<CsfdLookupResult> LookupAsync(
        string name,
        int? year,
        CsfdItemKind kind,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        _logger.LogInformation("ČSFD: zpracovávám '{Name}' ({Year})", name, year);

        if (string.IsNullOrWhiteSpace(sessionCookie))
        {
            _logger.LogWarning(
                "V konfiguraci pluginu ČSFD Rating není nastavená cookie (CsfdSessionCookie) - Anubis ochrana na csfd.cz pravděpodobně tenhle request zablokuje. Nastav ji v Dashboard -> Plugins -> ČSFD Rating.");
        }

        var searchUrl = BuildSearchUrl(name, year);
        _logger.LogInformation("ČSFD: search URL pro '{Name}' ({Year}): {Url}", name, year, searchUrl);

        string searchHtml;
        try
        {
            searchHtml = await GetHtmlAsync(searchUrl, sessionCookie, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "ČSFD vyhledávání selhalo pro '{Name}' ({Year}): {Url}", name, year, searchUrl);
            return new CsfdLookupResult(CsfdLookupStatus.Error, null, null);
        }

        if (CsfdHtmlParser.IsAnubisChallenge(searchHtml))
        {
            LogAnubisBlocked(searchUrl);
            return new CsfdLookupResult(CsfdLookupStatus.AnubisBlocked, null, null);
        }

        var relativeUrl = CsfdHtmlParser.FindFirstResultUrl(searchHtml, kind, year);
        if (relativeUrl is null)
        {
            _logger.LogInformation("ČSFD: odkaz nenalezen pro '{Name}' ({Year}) - žádný výsledek v sekci vyhledávání.", name, year);
            return new CsfdLookupResult(CsfdLookupStatus.NotFound, null, null);
        }

        var filmUrl = relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? relativeUrl
            : BaseUrl + relativeUrl;

        _logger.LogInformation("ČSFD: nalezený odkaz pro '{Name}' ({Year}): {Url}", name, year, filmUrl);

        string filmHtml;
        try
        {
            filmHtml = await GetHtmlAsync(filmUrl, sessionCookie, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Stažení ČSFD stránky {Url} selhalo", filmUrl);
            return new CsfdLookupResult(CsfdLookupStatus.Error, filmUrl, null);
        }

        if (CsfdHtmlParser.IsAnubisChallenge(filmHtml))
        {
            LogAnubisBlocked(filmUrl);
            return new CsfdLookupResult(CsfdLookupStatus.AnubisBlocked, filmUrl, null);
        }

        var rating = CsfdHtmlParser.ExtractRatingPercent(filmHtml);
        if (rating.HasValue)
        {
            _logger.LogInformation("ČSFD: nalezené hodnocení pro '{Name}' ({Year}): {Rating}%", name, year, rating);
            return new CsfdLookupResult(CsfdLookupStatus.Found, filmUrl, rating);
        }

        _logger.LogInformation(
            "ČSFD: stránka {Url} nalezena, ale element s hodnocením chybí nebo ukazuje \"?\" (zatím nedost hodnocení) - rating nenastavuji.",
            filmUrl);
        return new CsfdLookupResult(CsfdLookupStatus.Unrated, filmUrl, null);
    }

    private void LogAnubisBlocked(string url)
    {
        _logger.LogWarning(
            "ČSFD: ANUBIS CHALLENGE DETECTED na {Url} - cookie vypršela nebo je neplatná. Obnov ji v nastavení pluginu ČSFD Rating (Dashboard -> Plugins -> ČSFD Rating).",
            url);
    }

    private async Task<string> GetHtmlAsync(string url, string? sessionCookie, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(NamedClient.Default);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("cs-CZ,cs;q=0.9,en;q=0.8");

        if (!string.IsNullOrWhiteSpace(sessionCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
            _logger.LogInformation("ČSFD: request na {Url} odesílán s nastavenou Cookie hlavičkou.", url);
        }
        else
        {
            _logger.LogInformation("ČSFD: request na {Url} odesílán BEZ Cookie hlavičky (v konfiguraci není nic nastaveno).", url);
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "ČSFD: odpověď z {Url} - HTTP {StatusCode}, délka {Length} znaků. Prvních 500 znaků: {Snippet}",
            url,
            (int)response.StatusCode,
            html.Length,
            html.Length > 500 ? html[..500] : html);

        response.EnsureSuccessStatusCode();
        return html;
    }
}
