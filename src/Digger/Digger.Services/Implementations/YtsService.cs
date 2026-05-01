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
        UriBuilder uriBuilder = new UriBuilder(_ytsApiBaseUrl);
        NameValueCollection query = HttpUtility.ParseQueryString(uriBuilder.Query);
        foreach (KeyValuePair<string, string> item in _ytsParameters)
        {
            query[item.Key] = item.Value;
        }
        List<YtsMovieModel> movies = new List<YtsMovieModel>();
        using (HttpClientHandler httpClientHandler = new HttpClientHandler())
        {
            httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using HttpClient httpClient = new HttpClient(httpClientHandler);
            YtsResponse response = null;
            int page = 1;
            _ = DateTime.Now.Year;
            do
            {
                query["page"] = page.ToString();
                uriBuilder.Query = query.ToString();
                response = httpClient.GetFromJsonAsync<YtsResponse>(uriBuilder.Uri).GetAwaiter().GetResult();
                if (response != null)
                {
                    IEnumerable<Movie> moviesData = response.data.movies.Where((Movie m) => _ytsLanguages.Contains(m.language) && m.year >= DateTime.Now.Year - _ytsYearsBack && m.genres.Select((string g) => g.ToLower(System.Globalization.CultureInfo.CurrentCulture)).Intersect(_ytsGenres).Any());
                    if (moviesData != null && moviesData.Any())
                    {
                        foreach (Movie movieData in moviesData)
                        {
                            Torrent torrent = (from t in movieData.torrents
                                               where t.seeds >= _ytsMinimumSeeders && t.quality == _ytsParameters["quality"]
                                               orderby t.seeds descending
                                               select t).FirstOrDefault();
                            if (torrent != null)
                            {
                                movies.Add(new YtsMovieModel
                                {
                                    Id = movieData.id.ToString(),
                                    Name = movieData.title_english,
                                    Size = torrent.size_bytes,
                                    TorrentUrl = _ytsTorrentBaseUrl + torrent.url,
                                    PublishDate = DateTime.Parse(torrent.date_uploaded)
                                });
                            }
                        }
                    }
                }
                page++;
            }
            while (response != null && response.data.movies.Any((Movie m) => m.year >= DateTime.Now.Year - _ytsYearsBack));
        }
        return movies;
    }
}
