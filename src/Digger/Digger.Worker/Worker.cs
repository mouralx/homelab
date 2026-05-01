using Digger.Data.Common.Enums;
using Digger.Data.Context;
using Digger.Services.Contracts;
using Digger.Services.Models.Yts;

public class Worker : BackgroundService
{
    private readonly DiggerContext _data;

    private readonly ILogger<Worker> _logger;

    private readonly IYtsService _ytsService;

    private readonly ITransmissionService _transmissionService;

    private readonly IConfiguration _configuration;

    private readonly TimeSpan _stopTime;

    private readonly int _maxEnqueuedTorrents;

    private readonly int _maxEnqueuedRetries;

    private readonly long _maxAllocatedSpace;

    public Worker(DiggerContext diggerContext, ILogger<Worker> logger, IYtsService ytsService, ITransmissionService transmissionService, IConfiguration configuration)
    {
        _data = diggerContext;
        _logger = logger;
        _ytsService = ytsService;
        _transmissionService = transmissionService;
        _configuration = configuration;
        _stopTime = TimeSpan.FromMinutes(configuration.GetRequiredSection("StopTime").Get<int>());
        _maxEnqueuedTorrents = configuration.GetRequiredSection("MaxEnqueuedTorrents").Get<int>();
        _maxEnqueuedRetries = configuration.GetRequiredSection("MaxEnqueuedRetries").Get<int>();
        _maxAllocatedSpace = configuration.GetRequiredSection("MaxAllocatedSpace").Get<long>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
	_logger.LogInformation("Service Started Successfully");

        while (!stoppingToken.IsCancellationRequested)
        {
	    _logger.LogInformation("Digging for new movies...");
            GetNewMovies();
            _logger.LogInformation("Skipping oldest movies...");
            SkipOldestMovies();
	    _logger.LogInformation("Rolling out old movies and start new downloads...");
            CleanAndSyncMovies();
	    _logger.LogInformation("Retrying failed movies...");
            RetryFailedMovies();
	    _logger.LogInformation("Finished search.");
            await Task.Delay(_stopTime, stoppingToken);
        }
    }

    private void SkipOldestMovies()
    {
        IQueryable<Digger.Data.Entities.Movie> query = from m in _data.Movies
                                              where (int)m.LastKnownStatus == 7
                                              orderby m.PublishDate descending
                                              select m;
        if (query.Sum((Digger.Data.Entities.Movie m) => m.Size) > _maxAllocatedSpace)
        {
            int rowsToTake = 1;
            do
            {
                rowsToTake++;
            }
            while (query.Take(rowsToTake).Sum((Digger.Data.Entities.Movie m) => m.Size) < _maxAllocatedSpace);
            List<Digger.Data.Entities.Movie> downloableMovies = query.Take(rowsToTake).ToList();
            List<Digger.Data.Entities.Movie> moviesToSkip = _data.Movies.Where((Digger.Data.Entities.Movie m) => !downloableMovies.Contains(m)).ToList();
            moviesToSkip.ForEach(delegate (Digger.Data.Entities.Movie m)
            {
                m.LastKnownStatus = MovieStatus.Skipped;
            });
            _data.Movies.UpdateRange(moviesToSkip);
            _data.SaveChanges();
        }
    }

    private void CleanAndSyncMovies()
    {
        string[] downloadedDirectories = _transmissionService.ClenByStatuses(MovieStatus.Seeding, MovieStatus.PendingSeed);
        string[] stoppedDirectories = _transmissionService.ClenByStatuses(default(MovieStatus));
        if (downloadedDirectories.Length != 0)
        {
            List<Digger.Data.Entities.Movie> moviesToUpdate = _data.Movies.Where((Digger.Data.Entities.Movie m) => downloadedDirectories.Contains<string>(m.Path)).ToList();
            moviesToUpdate.ForEach(delegate (Digger.Data.Entities.Movie movie)
            {
                movie.LastKnownStatus = MovieStatus.Complete;
            });
            _data.Movies.UpdateRange(moviesToUpdate);
        }
        if (stoppedDirectories.Length != 0)
        {
            List<Digger.Data.Entities.Movie> moviesToUpdate2 = _data.Movies.Where((Digger.Data.Entities.Movie m) => stoppedDirectories.Contains<string>(m.Path)).ToList();
            moviesToUpdate2.ForEach(delegate (Digger.Data.Entities.Movie movie)
            {
                movie.DownloadAttempt++;
                movie.LastKnownStatus = ((movie.DownloadAttempt >= _maxEnqueuedRetries) ? MovieStatus.Failed : MovieStatus.NotEnqueued);
            });
            _data.Movies.UpdateRange(moviesToUpdate2);
        }
        _data.SaveChanges();
        if (_transmissionService.DownloadsCount() >= _maxEnqueuedTorrents)
        {
            return;
        }
        long currentAllocatedSpace = _data.Movies.Where((Digger.Data.Entities.Movie m) => (int)m.LastKnownStatus == 11 || (int)m.LastKnownStatus == 1 || (int)m.LastKnownStatus == 2 || (int)m.LastKnownStatus == 3 || (int)m.LastKnownStatus == 4 || (int)m.LastKnownStatus == 8).Sum((Digger.Data.Entities.Movie m) => m.Size);
        List<Digger.Data.Entities.Movie> moviesToEnqueue = (from m in _data.Movies
                                                   where (int)m.LastKnownStatus == 7
                                                   orderby m.PublishDate
                                                   select m).Take(_maxEnqueuedTorrents - _transmissionService.DownloadsCount()).ToList();
        long spaceAllocationNeeded = moviesToEnqueue.Select((Digger.Data.Entities.Movie m) => m.Size).Sum();
        if (spaceAllocationNeeded > _maxAllocatedSpace - currentAllocatedSpace)
        {
            int take = 1;
            IQueryable<Digger.Data.Entities.Movie> takenMovies = null;
            do
            {
                takenMovies = (from m in _data.Movies
                               where (int)m.LastKnownStatus == 11
                               orderby m.Timestamp
                               select m).Take(take);
                take++;
            }
            while (takenMovies.Sum((Digger.Data.Entities.Movie m) => m.Size) < spaceAllocationNeeded);
            List<Digger.Data.Entities.Movie> takenMoviesList = takenMovies.ToList();
            takenMoviesList.ForEach(delegate (Digger.Data.Entities.Movie m)
            {
                m.LastKnownStatus = MovieStatus.RolledOut;
                Directory.Delete(m.Path, recursive: true);
            });
            _data.Movies.UpdateRange(takenMoviesList);
            _data.SaveChanges();
        }
        moviesToEnqueue.ForEach(delegate (Digger.Data.Entities.Movie m)
        {
            try
            {
                _transmissionService.Download(m.TorrentUrl, m.Path);
            }
            catch (Exception)
            {
                m.LastKnownStatus = MovieStatus.Failed;
            }
            m.LastKnownStatus = MovieStatus.Enqueued;
        });
        _data.Movies.UpdateRange(moviesToEnqueue);
        _data.SaveChanges();
    }

    private void GetNewMovies()
    {
        List<YtsMovieModel> ytsMovies = _ytsService.GetMovies().ToList();
        ytsMovies.ForEach(delegate (YtsMovieModel m)
        {
            if (_data.Movies.Find(long.Parse(m.Id)) == null)
            {
                _data.Movies.Add(new Digger.Data.Entities.Movie
                {
                    Id = long.Parse(m.Id),
                    Name = m.Name,
                    TorrentUrl = m.TorrentUrl,
                    Path = Path.Combine(_configuration.GetRequiredSection("DownloadDirectory").Get<string>(), m.Id.ToString()),
                    Size = m.Size,
                    Timestamp = DateTime.Now,
                    PublishDate = m.PublishDate,
                    LastKnownStatus = MovieStatus.NotEnqueued
                });
            }
        });
        _data.SaveChanges();
    }

    private void RetryFailedMovies()
    {
        string[] downloadingPaths = _transmissionService.GetPaths();
        List<Digger.Data.Entities.Movie> enqueuedButNotDownloaded = _data.Movies.Where((Digger.Data.Entities.Movie m) => !downloadingPaths.Contains<string>(m.Path) && (int)m.LastKnownStatus == 8).ToList();
        enqueuedButNotDownloaded.ForEach(delegate (Digger.Data.Entities.Movie m)
        {
            m.LastKnownStatus = MovieStatus.NotEnqueued;
            m.DownloadAttempt++;
        });
        _data.Movies.UpdateRange(enqueuedButNotDownloaded);
        _data.SaveChanges();
    }
}
