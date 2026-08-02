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
    private readonly string[]? _ytsGenres;

    private readonly string[]? _ytsLanguages;

    private readonly int _ytsYearsBack;

    private readonly Dictionary<string, string>? _ytsParameters;

    private readonly string? _ytsApiBaseUrl;

    private readonly int _ytsMinimumSeeders;

    private readonly string? _ytsTorrentBaseUrl;

    private readonly bool _skipSslValidation;

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
        _skipSslValidation = configuration.GetValue<bool>("Yts:SkipSslValidation");
        _logger = logger;
    }

    public async Task<ICollection<YtsMovieModel>> GetMoviesAsync()
    {
        _logger.LogInformation("YtsService.GetMovies - Starting API request");
        _logger.LogInformation("Configuration - Genres: {Genres}, Languages: {Languages}, Years Back: {YearsBack}, Min Seeders: {MinSeeders}", 
            string.Join(",", _ytsGenres ?? Array.Empty<string>()), 
            string.Join(",", _ytsLanguages ?? Array.Empty<string>()), 
            _ytsYearsBack, 
            _ytsMinimumSeeders);
        
        UriBuilder uriBuilder = new UriBuilder(_ytsApiBaseUrl);
        NameValueCollection query = HttpUtility.ParseQueryString(uriBuilder.Query);
        foreach (KeyValuePair<string, string> item in _ytsParameters)
        {
            query[item.Key] = item.Value;
        }
        
        List<YtsMovieModel> movies = new List<YtsMovieModel>();
        using (HttpClientHandler httpClientHandler = new HttpClientHandler())
        {
            if (_skipSslValidation)
            {
                _logger.LogWarning("SSL certificate validation is disabled. This should only be used in development.");
                httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            using HttpClient httpClient = new HttpClient(httpClientHandler);
            YtsResponse response = null;
            int page = 1;
            _ = DateTime.Now.Year;
            
            do
            {
                try
                {
                    query["page"] = page.ToString();
                    uriBuilder.Query = query.ToString();
                    
                    _logger.LogDebug("Fetching page {Page} from YTS API: {Uri}", page, uriBuilder.Uri);
                    
                    response = await httpClient.GetFromJsonAsync<YtsResponse>(uriBuilder.Uri);
                    
                    if (response != null)
                    {
                        _logger.LogInformation("Page {Page}: Retrieved {Count} movies", page, response.data.movies.Count());
                        
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
                            _logger.LogInformation("Page {Page}: {FilteredCount} movies match filters", page, moviesData.Count());
                            
                            foreach (Movie movieData in moviesData)
                            {
                                Torrent torrent = (from t in movieData.torrents
                                                   where t.seeds >= _ytsMinimumSeeders && t.quality == _ytsParameters["quality"]
                                                   orderby t.seeds descending
                                                   select t).FirstOrDefault();
                                
                                if (torrent != null)
                                {
                                    _logger.LogDebug("Adding movie: {MovieTitle} (Quality: {Quality}, Seeds: {Seeds}, Size: {Size})", 
                                        movieData.title_english, torrent.quality, torrent.seeds, torrent.size_bytes);
                                    
                                    movies.Add(new YtsMovieModel
                                    {
                                        Id = movieData.id.ToString(),
                                        Name = movieData.title_english,
                                        Size = torrent.size_bytes,
                                        TorrentUrl = _ytsTorrentBaseUrl + torrent.url,
                                        PublishDate = DateTime.Parse(torrent.date_uploaded)
                                    });
                                }
                                else
                                {
                                    _logger.LogDebug("No torrent found matching criteria for movie: {MovieTitle}", movieData.title_english);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug("No movies on page {Page} match the filters", page);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Empty response from YTS API on page {Page}", page);
                    }
                    
                    page++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching page {Page} from YTS API", page);
                    throw;
                }
            }
            while (response != null && response.data.movies.Any((Movie m) => m.year >= DateTime.Now.Year - _ytsYearsBack));
        }
        
        _logger.LogInformation("YtsService.GetMovies completed - Total movies fetched: {Count}", movies.Count);
        return movies;
    }
}
