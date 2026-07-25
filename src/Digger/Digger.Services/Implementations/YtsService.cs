using Digger.Services.Contracts;
using Digger.Services.Models.Yts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Specialized;
using System.Net.Http.Json;
using System.Web;

namespace Digger.Services.Implementations;

public class YtsService : IYtsService
{
    private static readonly HttpClient _httpClient = new HttpClient();

    private readonly string[]? _ytsGenres;

    private readonly string[]? _ytsLanguages;

    private readonly int _ytsYearsBack;

    private readonly Dictionary<string, string>? _ytsParameters;

    private readonly string? _ytsApiBaseUrl;

    private readonly int _ytsMinimumSeeders;

    private readonly string? _ytsTorrentBaseUrl;

    private readonly ILogger<YtsService> _logger;

    public YtsService(ILogger<YtsService> logger, IConfiguration configuration)
    {
        _ytsGenres = configuration.GetRequiredSection("Yts:Genres").Get<string[]>();
        _ytsLanguages = configuration.GetRequiredSection("Yts:Languages").Get<string[]>();
        _ytsYearsBack = configuration.GetRequiredSection("Yts:YearsBack").Get<int>();
        _ytsParameters = configuration.GetRequiredSection("Yts:Parameters").Get<Dictionary<string, string>>();
        _ytsApiBaseUrl = configuration.GetRequiredSection("Yts:ApiUrl").Get<string>();
        _ytsMinimumSeeders = configuration.GetRequiredSection("Yts:MinimumSeeders").Get<int>();
        _ytsTorrentBaseUrl = configuration.GetRequiredSection("Yts:TorrentBaseUrl").Get<string>();
        _logger = logger;
    }

    public ICollection<YtsMovieModel> GetMovies()
    {
        _logger.LogInformation("YtsService.GetMovies - Starting API request");
        _logger.LogInformation("Configuration - Genres: {Genres}, Languages: {Languages}, Years Back: {YearsBack}, Min Seeders: {MinSeeders}", 
            string.Join(",", _ytsGenres ?? Array.Empty<string>()), 
            string.Join(",", _ytsLanguages ?? Array.Empty<string>()), 
            _ytsYearsBack, 
            _ytsMinimumSeeders);

        var requestStartTotal = DateTime.UtcNow;
        UriBuilder uriBuilder = new UriBuilder(_ytsApiBaseUrl);
        NameValueCollection query = HttpUtility.ParseQueryString(uriBuilder.Query);
        foreach (KeyValuePair<string, string> item in _ytsParameters)
        {
            query[item.Key] = item.Value;
        }

        List<YtsMovieModel> movies = new List<YtsMovieModel>();
        using (HttpClientHandler httpClientHandler = new HttpClientHandler())
        {
            using HttpClient httpClient = new HttpClient(httpClientHandler);
            YtsResponse response = null;
            int page = 1;

            do
            {
                try
                {
                    query["page"] = page.ToString();
                    uriBuilder.Query = query.ToString();

                    _logger.LogInformation("YTS API - Fetching page {Page}: {Uri}", page, uriBuilder.Uri);

                    var requestStart = DateTime.UtcNow;
                    response = _httpClient.GetFromJsonAsync<YtsResponse>(uriBuilder.Uri).GetAwaiter().GetResult();
                    var requestElapsed = DateTime.UtcNow - requestStart;
                    _logger.LogInformation("YTS API - Page {Page} responded in {Elapsed:F1}s", page, requestElapsed.TotalSeconds);

                    if (response != null)
                    {
                        var totalOnPage = response.data.movies.Count();

                        var langOk = response.data.movies.Count(m => _ytsLanguages.Contains(m.language));
                        var yearOk = response.data.movies.Count(m => m.year >= DateTime.Now.Year - _ytsYearsBack);
                        var genreOk = response.data.movies.Count(m => m.genres.Select(g => g.ToLower(System.Globalization.CultureInfo.CurrentCulture)).Intersect(_ytsGenres).Any());
                        _logger.LogInformation("YTS API - Page {Page}: {Total} movies — Language OK: {LangOk}, Year OK: {YearOk}, Genre OK: {GenreOk}",
                            page, totalOnPage, langOk, yearOk, genreOk);

                        IEnumerable<Movie> moviesData = response.data.movies.Where((Movie m) =>
                        {
                            bool langMatch = _ytsLanguages.Contains(m.language);
                            bool yearMatch = m.year >= DateTime.Now.Year - _ytsYearsBack;
                            bool genreMatch = m.genres.Select((string g) => g.ToLower(System.Globalization.CultureInfo.CurrentCulture)).Intersect(_ytsGenres).Any();

                            if (!langMatch) _logger.LogDebug("Movie filtered out - Language not match: {MovieTitle} (Language: {Language})", m.title_english, m.language);
                            if (!yearMatch) _logger.LogDebug("Movie filtered out - Year too old: {MovieTitle} (Year: {Year})", m.title_english, m.year);
                            if (!genreMatch) _logger.LogDebug("Movie filtered out - Genre not match: {MovieTitle} (Genres: {Genres})", m.title_english, string.Join(",", m.genres));

                            return langMatch && yearMatch && genreMatch;
                        });

                        if (moviesData != null && moviesData.Any())
                        {
                            _logger.LogInformation("YTS API - Page {Page}: {FilteredCount}/{TotalOnPage} movies passed all filters", page, moviesData.Count(), totalOnPage);

                            var seedOkCount = 0;
                            var seedSkipCount = 0;
                            foreach (Movie movieData in moviesData)
                            {
                                Torrent torrent = (from t in movieData.torrents
                                                   where t.seeds >= _ytsMinimumSeeders && t.quality == _ytsParameters["quality"]
                                                   orderby t.seeds descending
                                                   select t).FirstOrDefault();

                                if (torrent != null)
                                {
                                    _logger.LogInformation("YTS API - Adding movie: {MovieTitle} (Quality: {Quality}, Seeds: {Seeds}, Size: {Size} bytes)",
                                        movieData.title_english, torrent.quality, torrent.seeds, torrent.size_bytes);

                                    movies.Add(new YtsMovieModel
                                    {
                                        Id = movieData.id.ToString(),
                                        Name = movieData.title_english,
                                        Size = torrent.size_bytes,
                                        TorrentUrl = _ytsTorrentBaseUrl + torrent.url,
                                        PublishDate = DateTime.Parse(torrent.date_uploaded)
                                    });
                                    seedOkCount++;
                                }
                                else
                                {
                                    _logger.LogInformation("YTS API - No suitable torrent for: {MovieTitle} (needs min {MinSeeders} seeders, quality {Quality})",
                                        movieData.title_english, _ytsMinimumSeeders, _ytsParameters["quality"]);
                                    seedSkipCount++;
                                }
                            }
                            _logger.LogInformation("YTS API - Page {Page}: {Added} movies added, {Skipped} skipped (seeder/quality filter)",
                                page, seedOkCount, seedSkipCount);
                        }
                        else
                        {
                            _logger.LogInformation("YTS API - Page {Page}: 0 movies passed all filters (out of {Total})", page, totalOnPage);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("YTS API - Page {Page}: Empty response", page);
                    }

                    page++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "YTS API - Error fetching page {Page}", page);
                    throw;
                }
            }
            while (response != null && response.data.movies.Any((Movie m) => m.year >= DateTime.Now.Year - _ytsYearsBack));

            var totalPages = page - 1;
            var totalElapsed = DateTime.UtcNow - requestStartTotal;
            _logger.LogInformation("YTS API - Completed: {MovieCount} movies from {PageCount} pages in {Elapsed:F1}s", movies.Count, totalPages, totalElapsed.TotalSeconds);
        }

        return movies;
    }
}
