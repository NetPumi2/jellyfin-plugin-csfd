using System;
using System.IO;
using Jellyfin.Plugin.Csfd.Csfd;
using Xunit;

namespace Jellyfin.Plugin.Csfd.Tests;

public class CsfdHtmlParserTests
{
    private static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return File.ReadAllText(path);
    }

    [Fact]
    public void FindFirstResultUrl_ReturnsFirstMovieLink()
    {
        var html = ReadFixture("search-results.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Movie);

        Assert.Equal("/film/1091993-oppenheimer/", url);
    }

    [Fact]
    public void FindFirstResultUrl_ReturnsFirstSeriesLink()
    {
        var html = ReadFixture("search-results.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Series);

        Assert.Equal("/film/86620-oppenheimer/", url);
    }

    [Fact]
    public void FindFirstResultUrl_PrefersResultMatchingYear()
    {
        var html = ReadFixture("search-results-same-title-diff-years.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Movie, 2025);

        Assert.Equal("/film/222222-roofman-2025/", url);
    }

    [Fact]
    public void FindFirstResultUrl_PrefersEarlierYearMatch()
    {
        var html = ReadFixture("search-results-same-title-diff-years.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Movie, 2013);

        Assert.Equal("/film/111111-roofman-2013/", url);
    }

    [Fact]
    public void FindFirstResultUrl_FallsBackToFirstResult_WhenNoYearMatches()
    {
        var html = ReadFixture("search-results-same-title-diff-years.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Movie, 1999);

        Assert.Equal("/film/111111-roofman-2013/", url);
    }

    [Fact]
    public void FindFirstResultUrl_FallsBackToFirstResult_WhenYearNotProvided()
    {
        var html = ReadFixture("search-results-same-title-diff-years.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Movie);

        Assert.Equal("/film/111111-roofman-2013/", url);
    }

    [Fact]
    public void FindFirstResultUrl_FindsResult_WithMinimalHeaderMarkupAndNoSpecificInnerClasses()
    {
        // Regression test: the parser must not depend on specific inner class names like
        // "film-title-nooverflow"/"film-title-name" - only on the outer
        // <article class="... article-poster ...·"> wrapper and the "/film/{id}-" href shape,
        // since those inner classes can (and did) drift without notice.
        var html = ReadFixture("search-results-minimal-header-markup.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Movie, 2018);

        Assert.Equal("/film/303030-robin-hood/", url);
    }

    [Fact]
    public void FindFirstResultUrl_ReturnsNull_WhenSectionMissing()
    {
        var html = ReadFixture("search-results-no-movies.html");

        var url = CsfdHtmlParser.FindFirstResultUrl(html, CsfdItemKind.Movie);

        Assert.Null(url);
    }

    [Fact]
    public void ExtractRatingPercent_ParsesNumericRating()
    {
        var html = ReadFixture("film-detail-rated.html");

        var rating = CsfdHtmlParser.ExtractRatingPercent(html);

        Assert.Equal(63, rating);
    }

    [Fact]
    public void ExtractRatingPercent_ReturnsNull_WhenRatingIsQuestionMark()
    {
        var html = ReadFixture("film-detail-unrated.html");

        var rating = CsfdHtmlParser.ExtractRatingPercent(html);

        Assert.Null(rating);
    }

    [Fact]
    public void ExtractRatingPercent_ReturnsNull_WhenElementMissing()
    {
        var rating = CsfdHtmlParser.ExtractRatingPercent("<html><body>nothing here</body></html>");

        Assert.Null(rating);
    }

    [Fact]
    public void IsAnubisChallenge_DetectsRealChallengePage()
    {
        var html = ReadFixture("anubis-challenge.html");

        Assert.True(CsfdHtmlParser.IsAnubisChallenge(html));
    }

    [Fact]
    public void IsAnubisChallenge_ReturnsFalse_ForRealContentPage()
    {
        var html = ReadFixture("film-detail-rated.html");

        Assert.False(CsfdHtmlParser.IsAnubisChallenge(html));
    }

    [Theory]
    [InlineData("Superman", 2025, "https://www.csfd.cz/hledat/?q=Superman+2025")]
    [InlineData("Přátelé", null, "https://www.csfd.cz/hledat/?q=P%C5%99%C3%A1tel%C3%A9")]
    public void BuildSearchUrl_EncodesNameAndYear(string name, int? year, string expected)
    {
        var url = CsfdClient.BuildSearchUrl(name, year);

        Assert.Equal(expected, url);
    }
}
