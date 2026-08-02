using Digger.Data.Common.Enums;
using Digger.Data.Context;
using Digger.Services.Contracts;
using Digger.Services.Models.Yts;

public class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<Worker> _logger;

    private readonly IYtsService _ytsService;

    private readonly ITransmissionService _transmissionService;

    private readonly IConfiguration _configuration;

    private readonly TimeSpan _stopTime;

    private readonly int _maxEnqueuedTorrents;

    private readonly int _maxEnqueuedRetries;

    private readonly long _maxAllocatedSpace;

    public Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger, IYtsService ytsService, ITransmissionService transmissionService, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
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
        _logger.LogInformation("Worker Service Started Successfully");
        _logger.LogInformation("Configuration - StopTime: {StopTime}ms, MaxEnqueuedTorrents: {MaxEnqueuedTorrents}, MaxEnqueuedRetries: {MaxEnqueuedRetries}, MaxAllocatedSpace: {MaxAllocatedSpace} bytes", 
            _stopTime.TotalMilliseconds, _maxEnqueuedTorrents, _maxEnqueuedRetries, _maxAllocatedSpace);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting movie discovery cycle...");
                
                _logger.LogInformation("Step 1: Digging for new movies...");
                await GetNewMoviesAsync();
                
                _logger.LogInformation("Step 2: Skipping oldest movies...");
                await SkipOldestMoviesAsync();
                
                _logger.LogInformation("Step 3: Rolling out old movies and starting new downloads...");
                await CleanAndSyncMoviesAsync();
                
                _logger.LogInformation("Step 4: Retrying failed movies...");
                await RetryFailedMoviesAsync();
                
                _logger.LogInformation("Finished search cycle. Next cycle in {StopTime}ms", _stopTime.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during worker cycle");
            }
            
            await Task.Delay(_stopTime, stoppingToken);
        }
        
        _logger.LogInformation("Worker service stopping...");
    }

    private async Task SkipOldestMoviesAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var _data = scope.ServiceProvider.GetRequiredService<DiggerContext>();
            
            var allMovies = await _data.Movies
                .Where(m => m.LastKnownStatus == MovieStatus.NotEnqueued)
                .OrderByDescending(m => m.PublishDate)
                .ToListAsync();
            
            var totalSize = allMovies.Sum(m => m.Size);
            _logger.LogInformation("SkipOldestMovies - Total ready-to-download size: {TotalSize} bytes, Max allowed: {MaxAllocatedSpace} bytes", 
                totalSize, _maxAllocatedSpace);
            
            if (totalSize > _maxAllocatedSpace)
            {
                _logger.LogWarning("Allocated space exceeded by {Excess} bytes. Marking oldest movies as skipped.",
                    totalSize - _maxAllocatedSpace);
                
                long runningSum = 0;
                int rowsToKeep = 0;
                foreach (var movie in allMovies)
                {
                    runningSum += movie.Size;
                    rowsToKeep++;
                    if (runningSum >= _maxAllocatedSpace)
                    {
                        break;
                    }
                }
                
                var moviesToKeep = allMovies.Take(rowsToKeep).ToList();
                var moviesToSkip = allMovies.Where(m => !moviesToKeep.Contains(m)).ToList();
                
                _logger.LogInformation("Skipping {Count} oldest movies to free up space", moviesToSkip.Count);
                moviesToSkip.ForEach(delegate (Digger.Data.Entities.Movie m)
                {
                    _logger.LogDebug("Marking movie as skipped: {MovieName}", m.Name);
                    m.LastKnownStatus = MovieStatus.Skipped;
                });
                _data.Movies.UpdateRange(moviesToSkip);
                await _data.SaveChangesAsync();
            }
            else
            {
                _logger.LogInformation("Allocated space is within limits. No movies need to be skipped.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while skipping oldest movies");
            throw;
        }
    }

    private async Task CleanAndSyncMoviesAsync()
    {
        try
        {
            _logger.LogInformation("Starting CleanAndSyncMovies operation...");
            
            using var scope = _scopeFactory.CreateScope();
            var _data = scope.ServiceProvider.GetRequiredService<DiggerContext>();
            
            string[] downloadedDirectories = _transmissionService.CleanByStatuses(MovieStatus.Seeding, MovieStatus.PendingSeed);
            _logger.LogInformation("Found {Count} completed/seeding torrents", downloadedDirectories.Length);
            
            string[] stoppedDirectories = _transmissionService.CleanByStatuses(default(MovieStatus));
            _logger.LogInformation("Found {Count} stopped torrents", stoppedDirectories.Length);
            
            if (downloadedDirectories.Length != 0)
            {
                _logger.LogInformation("Processing {Count} completed downloads...", downloadedDirectories.Length);
                List<Digger.Data.Entities.Movie> moviesToUpdate = _data.Movies.Where((Digger.Data.Entities.Movie m) => downloadedDirectories.Contains<string>(m.Path)).ToList();
                _logger.LogDebug("Updating {Count} movies to Complete status", moviesToUpdate.Count);
                moviesToUpdate.ForEach(delegate (Digger.Data.Entities.Movie movie)
                {
                    _logger.LogDebug("Marking movie as complete: {MovieName}", movie.Name);
                    movie.LastKnownStatus = MovieStatus.Complete;
                });
                _data.Movies.UpdateRange(moviesToUpdate);
            }
            
            if (stoppedDirectories.Length != 0)
            {
                _logger.LogInformation("Processing {Count} stopped/failed torrents...", stoppedDirectories.Length);
                List<Digger.Data.Entities.Movie> moviesToUpdate2 = _data.Movies.Where((Digger.Data.Entities.Movie m) => stoppedDirectories.Contains<string>(m.Path)).ToList();
                moviesToUpdate2.ForEach(delegate (Digger.Data.Entities.Movie movie)
                {
                    movie.DownloadAttempt++;
                    if (movie.DownloadAttempt >= _maxEnqueuedRetries)
                    {
                        _logger.LogWarning("Movie exceeded max retries: {MovieName} ({Attempt}/{MaxAttempts})", 
                            movie.Name, movie.DownloadAttempt, _maxEnqueuedRetries);
                        movie.LastKnownStatus = MovieStatus.Failed;
                    }
                    else
                    {
                        _logger.LogInformation("Retry movie download: {MovieName} ({Attempt}/{MaxAttempts})", 
                            movie.Name, movie.DownloadAttempt, _maxEnqueuedRetries);
                        movie.LastKnownStatus = MovieStatus.NotEnqueued;
                    }
                });
                _data.Movies.UpdateRange(moviesToUpdate2);
            }
            
            await _data.SaveChangesAsync();
            
            int currentDownloads = _transmissionService.DownloadsCount();
            _logger.LogInformation("Current active downloads: {CurrentDownloads}/{MaxDownloads}", currentDownloads, _maxEnqueuedTorrents);
            
            if (_transmissionService.DownloadsCount() >= _maxEnqueuedTorrents)
            {
                _logger.LogInformation("Max enqueu­ed torrents reached. Skipping new torrent enqueueing.");
                return;
            }
            
            long currentAllocatedSpace = _data.Movies.Where((Digger.Data.Entities.Movie m) => (int)m.LastKnownStatus == 11 || (int)m.LastKnownStatus == 1 || (int)m.LastKnownStatus == 2 || (int)m.LastKnownStatus == 3 || (int)m.LastKnownStatus == 4 || (int)m.LastKnownStatus == 8).Sum((Digger.Data.Entities.Movie m) => m.Size);
            _logger.LogInformation("Current allocated space: {CurrentSpace} bytes / {MaxSpace} bytes", currentAllocatedSpace, _maxAllocatedSpace);
            
            List<Digger.Data.Entities.Movie> moviesToEnqueue = (from m in _data.Movies
                                                       where (int)m.LastKnownStatus == 7
                                                       orderby m.PublishDate
                                                       select m).Take(_maxEnqueuedTorrents - _transmissionService.DownloadsCount()).ToList();
            
            _logger.LogInformation("Found {Count} movies ready to download", moviesToEnqueue.Count);
            
            long spaceAllocationNeeded = moviesToEnqueue.Select((Digger.Data.Entities.Movie m) => m.Size).Sum();
            _logger.LogInformation("Space needed for new downloads: {SpaceNeeded} bytes", spaceAllocationNeeded);
            
            if (spaceAllocationNeeded > _maxAllocatedSpace - currentAllocatedSpace)
            {
                _logger.LogWarning("Insufficient space. Need {SpaceNeeded} but only {AvailableSpace} available. Rolling out oldest completed movies...", 
                    spaceAllocationNeeded, _maxAllocatedSpace - currentAllocatedSpace);
                
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
                _logger.LogInformation("Rolling out {Count} oldest movies to free up space", takenMoviesList.Count);
                
                takenMoviesList.ForEach(delegate (Digger.Data.Entities.Movie m)
                {
                    try
                    {
                        _logger.LogInformation("Rolling out movie: {MovieName} at {Path}", m.Name, m.Path);
                        m.LastKnownStatus = MovieStatus.RolledOut;
                        Directory.Delete(m.Path, recursive: true);
                        _logger.LogInformation("Successfully deleted movie directory: {Path}", m.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete movie directory: {Path}", m.Path);
                    }
                });
                _data.Movies.UpdateRange(takenMoviesList);
                await _data.SaveChangesAsync();
            }
            
            _logger.LogInformation("Enqueueing {Count} movies for download...", moviesToEnqueue.Count);
            moviesToEnqueue.ForEach(delegate (Digger.Data.Entities.Movie m)
            {
                try
                {
                    _logger.LogInformation("Starting download for movie: {MovieName} (Size: {Size} bytes)", m.Name, m.Size);
                    _transmissionService.Download(m.TorrentUrl, m.Path);
                    m.LastKnownStatus = MovieStatus.Enqueued;
                    _logger.LogInformation("Successfully enqueued movie: {MovieName}", m.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue movie: {MovieName}. Marking as failed.", m.Name);
                    m.LastKnownStatus = MovieStatus.Failed;
                }
            });
            
            _data.Movies.UpdateRange(moviesToEnqueue);
            await _data.SaveChangesAsync();
            
            _logger.LogInformation("CleanAndSyncMovies operation completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during CleanAndSyncMovies operation");
            throw;
        }
    }

    private async Task GetNewMoviesAsync()
    {
        try
        {
            _logger.LogInformation("Fetching movies from YTS API...");
            var newMovies = await _ytsService.GetMoviesAsync();
            _logger.LogInformation("Fetched {Count} movies from YTS", newMovies.Count);
            
            using var scope = _scopeFactory.CreateScope();
            var _data = scope.ServiceProvider.GetRequiredService<DiggerContext>();
            
            var addedCount = 0;
            var skippedCount = 0;
            
            foreach (var movie in newMovies)
            {
                var movieId = long.Parse(movie.Id);
                var existingMovie = _data.Movies.FirstOrDefault(m => m.Id == movieId);
                if (existingMovie == null)
                {
                    _logger.LogDebug("Adding new movie: {MovieName} (Size: {Size} bytes, Published: {PublishDate})", 
                        movie.Name, movie.Size, movie.PublishDate);
                    
                    var newMovie = new Digger.Data.Entities.Movie
                    {
                        Id = movieId,
                        Name = movie.Name,
                        Size = movie.Size,
                        TorrentUrl = movie.TorrentUrl,
                        PublishDate = movie.PublishDate,
                        Path = Path.Combine("/downloads/movies", movie.Name.Replace(" ", "_")),
                        LastKnownStatus = Digger.Data.Common.Enums.MovieStatus.NotEnqueued,
                        Timestamp = DateTime.UtcNow
                    };
                    
                    _data.Movies.Add(newMovie);
                    addedCount++;
                }
                else
                {
                    _logger.LogDebug("Movie already exists in database: {MovieName}", movie.Name);
                    skippedCount++;
                }
            }
            
            if (addedCount > 0 || skippedCount > 0)
            {
                await _data.SaveChangesAsync();
                _logger.LogInformation("GetNewMovies completed - Added: {AddedCount}, Skipped: {SkippedCount}", addedCount, skippedCount);
            }
            else
            {
                _logger.LogInformation("No new movies found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching new movies from YTS");
            throw;
        }
    }

    private async Task RetryFailedMoviesAsync()
    {
        try
        {
            _logger.LogInformation("Checking for failed movies to retry...");
            
            using var scope = _scopeFactory.CreateScope();
            var _data = scope.ServiceProvider.GetRequiredService<DiggerContext>();
            
            var failedMovies = _data.Movies.Where(m => m.LastKnownStatus == MovieStatus.Failed).ToList();
            _logger.LogInformation("Found {Count} failed movies", failedMovies.Count);
            
            foreach (var movie in failedMovies)
            {
                _logger.LogDebug("Resetting failed movie for retry: {MovieName} (Attempts: {Attempts})", 
                    movie.Name, movie.DownloadAttempt);
                movie.LastKnownStatus = MovieStatus.NotEnqueued;
                movie.DownloadAttempt = 0;
            }
            
            if (failedMovies.Count > 0)
            {
                _data.Movies.UpdateRange(failedMovies);
                await _data.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrying failed movies");
            throw;
        }
    }
}
