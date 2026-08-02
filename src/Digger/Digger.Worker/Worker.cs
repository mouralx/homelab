using System.IO;
using Digger.Data.Common.Enums;
using Microsoft.EntityFrameworkCore;
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

    private readonly long _diskSpaceSafetyMarginBytes;

    private readonly bool _cleanIncompleteBeforeDownload;

    private readonly string _downloadDirectory;

    private readonly string _incompleteDirectory;

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
        
        // New: disk space safety margin (default 50GB)
        var safetyMarginGb = configuration.GetValue<int>("DiskSpaceSafetyMarginGb", 50);
        _diskSpaceSafetyMarginBytes = safetyMarginGb * 1024L * 1024L * 1024L;
        
        // New: proactive incomplete cleanup
        _cleanIncompleteBeforeDownload = configuration.GetValue<bool>("CleanIncompleteBeforeDownload", true);
        _downloadDirectory = configuration.GetValue<string>("DownloadDirectory") ?? "/downloads/movies";
        _incompleteDirectory = configuration.GetSection("Transmission:IncompleteDir").Value ?? "/incomplete";
        
        _logger.LogInformation("Worker initialized - SafetyMargin: {SafetyMarginGb}GB, CleanIncomplete: {CleanIncomplete}", 
            safetyMarginGb, _cleanIncompleteBeforeDownload);
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
                await GetNewMoviesAsync(stoppingToken);
                
                _logger.LogInformation("Step 2: Rolling out old movies and starting new downloads...");
                await CleanAndSyncMoviesAsync(stoppingToken);
                
                _logger.LogInformation("Step 3: Retrying failed movies...");
                await RetryFailedMoviesAsync(stoppingToken);
                
                _logger.LogInformation("Finished search cycle. Next cycle in {StopTime}ms", _stopTime.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker service stopping due to cancellation request.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during worker cycle");
            }
            
            await Task.Delay(_stopTime, stoppingToken);
        }
        
        _logger.LogInformation("Worker service stopping...");
    }

    /// <summary>
    /// Checks actual filesystem free space on the download volume.
    /// Returns true if there is enough space for the requested amount.
    /// </summary>
    private bool HasEnoughDiskSpace(long requestedBytes)
    {
        try
        {
            var driveInfo = new DriveInfo(_downloadDirectory);
            var freeBytes = driveInfo.AvailableFreeSpace;
            var requiredBytes = requestedBytes + _diskSpaceSafetyMarginBytes;
            
            _logger.LogInformation("Disk space check - Free: {FreeGb:F2}GB, Requested: {RequestedGb:F2}GB, Safety margin: {SafetyMarginGb}GB, Total needed: {TotalNeededGb:F2}GB",
                freeBytes / (1024.0 * 1024.0 * 1024.0),
                requestedBytes / (1024.0 * 1024.0 * 1024.0),
                _diskSpaceSafetyMarginBytes / (1024.0 * 1024.0 * 1024.0),
                requiredBytes / (1024.0 * 1024.0 * 1024.0));
            
            if (freeBytes < requiredBytes)
            {
                _logger.LogWarning("Insufficient disk space! Free: {FreeGb:F2}GB, Need: {TotalNeededGb:F2}GB (requested + safety margin)",
                    freeBytes / (1024.0 * 1024.0 * 1024.0),
                    requiredBytes / (1024.0 * 1024.0 * 1024.0));
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking disk space on {Directory}. Assuming insufficient space.", _downloadDirectory);
            return false;
        }
    }

    /// <summary>
    /// Proactively cleans the incomplete downloads directory to free space.
    /// </summary>
    private void CleanIncompleteDirectory()
    {
        if (!_cleanIncompleteBeforeDownload)
            return;

        try
        {
            if (!Directory.Exists(_incompleteDirectory))
            {
                _logger.LogDebug("Incomplete directory does not exist: {IncompleteDir}", _incompleteDirectory);
                return;
            }

            var files = Directory.GetFiles(_incompleteDirectory);
            if (files.Length == 0)
            {
                _logger.LogDebug("Incomplete directory is already empty");
                return;
            }

            long totalSize = 0;
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                totalSize += fileInfo.Length;
                File.Delete(file);
            }

            _logger.LogInformation("Cleaned incomplete directory - Deleted {Count} files, freed {SizeMb:F2}MB",
                files.Length, totalSize / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning incomplete directory: {IncompleteDir}", _incompleteDirectory);
        }
    }

    private async Task SkipOldestMoviesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var _data = scope.ServiceProvider.GetRequiredService<DiggerContext>();
            
            var allMovies = await _data.Movies
                .Where(m => m.LastKnownStatus == MovieStatus.NotEnqueued)
                .OrderByDescending(m => m.PublishDate)
                .ToListAsync(cancellationToken);
            
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
                await _data.SaveChangesAsync(cancellationToken);
            }
            else
            {
                _logger.LogInformation("Allocated space is within limits. No movies need to be skipped.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while skipping oldest movies");
            throw;
        }
    }

    private async Task CleanAndSyncMoviesAsync(CancellationToken cancellationToken)
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
            
            int currentDownloads = _transmissionService.DownloadsCount();
            _logger.LogInformation("Current active downloads: {CurrentDownloads}/{MaxDownloads}", currentDownloads, _maxEnqueuedTorrents);
            
            if (currentDownloads >= _maxEnqueuedTorrents)
            {
                _logger.LogInformation("Max enqueued torrents reached. Skipping new torrent enqueueing.");
                await _data.SaveChangesAsync(cancellationToken);
                return;
            }
            
            long currentAllocatedSpace = _data.Movies.Where(m => 
                m.LastKnownStatus == MovieStatus.Complete ||
                m.LastKnownStatus == MovieStatus.Stopped ||
                m.LastKnownStatus == MovieStatus.PendingCheck ||
                m.LastKnownStatus == MovieStatus.Checking ||
                m.LastKnownStatus == MovieStatus.PendingDownload ||
                m.LastKnownStatus == MovieStatus.Downloading ||
                m.LastKnownStatus == MovieStatus.Enqueued)
                .Sum(m => m.Size);
            _logger.LogInformation("Current allocated space: {CurrentSpace} bytes / {MaxSpace} bytes", currentAllocatedSpace, _maxAllocatedSpace);
            
            int slotsAvailable = _maxEnqueuedTorrents - currentDownloads;
            var moviesToEnqueue = await _data.Movies
                .Where(m => m.LastKnownStatus == MovieStatus.NotEnqueued)
                .OrderBy(m => m.PublishDate)
                .Take(slotsAvailable)
                .ToListAsync(cancellationToken);
            
            _logger.LogInformation("Found {Count} movies ready to download", moviesToEnqueue.Count);
            
            long spaceAllocationNeeded = moviesToEnqueue.Sum(m => m.Size);
            _logger.LogInformation("Space needed for new downloads: {SpaceNeeded} bytes", spaceAllocationNeeded);
            
            if (spaceAllocationNeeded > _maxAllocatedSpace - currentAllocatedSpace)
            {
                _logger.LogWarning("Insufficient space. Need {SpaceNeeded} but only {AvailableSpace} available. Rolling out oldest completed movies...", 
                    spaceAllocationNeeded, _maxAllocatedSpace - currentAllocatedSpace);
                
                var completedMovies = await _data.Movies
                    .Where(m => m.LastKnownStatus == MovieStatus.Complete)
                    .OrderBy(m => m.Timestamp)
                    .ToListAsync(cancellationToken);
                
                long runningSum = 0;
                var moviesToRollOut = new List<Digger.Data.Entities.Movie>();
                foreach (var movie in completedMovies)
                {
                    moviesToRollOut.Add(movie);
                    runningSum += movie.Size;
                    if (runningSum >= spaceAllocationNeeded)
                    {
                        break;
                    }
                }
                
                _logger.LogInformation("Rolling out {Count} oldest movies to free up space", moviesToRollOut.Count);
                
                moviesToRollOut.ForEach(delegate (Digger.Data.Entities.Movie m)
                {
                    _logger.LogInformation("Marking movie for cleanup: {MovieName}", m.Name);
                    m.LastKnownStatus = MovieStatus.RolledOut;
                });
                _data.Movies.UpdateRange(moviesToRollOut);
                
                // Recalculate after rolling out to get accurate space check
                currentAllocatedSpace = _data.Movies.Where(m => 
                    m.LastKnownStatus == MovieStatus.Complete ||
                    m.LastKnownStatus == MovieStatus.Stopped ||
                    m.LastKnownStatus == MovieStatus.PendingCheck ||
                    m.LastKnownStatus == MovieStatus.Checking ||
                    m.LastKnownStatus == MovieStatus.PendingDownload ||
                    m.LastKnownStatus == MovieStatus.Downloading ||
                    m.LastKnownStatus == MovieStatus.Enqueued)
                    .Sum(m => m.Size);
                _logger.LogInformation("After rollout - Allocated space: {CurrentSpace} bytes / {MaxSpace} bytes", currentAllocatedSpace, _maxAllocatedSpace);
            }
            
            // Secondary trigger: force rollout when disk is critically low,
            // even if the logical allocation budget has room
            try
            {
                var driveInfo = new DriveInfo(_downloadDirectory);
                var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                _logger.LogInformation("Disk space check for rollout trigger - Free: {FreeGb:F2}GB", freeGb);
                
                if (freeGb < 10)
                {
                    _logger.LogWarning("Disk critically low ({FreeGb:F2}GB free). Forcing rollout of oldest completed movies...", freeGb);
                    
                    var completedMoviesToRoll = await _data.Movies
                        .Where(m => m.LastKnownStatus == MovieStatus.Complete)
                        .OrderBy(m => m.Timestamp)
                        .ToListAsync(cancellationToken);
                    
                    if (completedMoviesToRoll.Count > 0)
                    {
                        // Roll out half of completed movies to free meaningful space
                        var countToRoll = Math.Max(1, completedMoviesToRoll.Count / 2);
                        var forcedRollOut = completedMoviesToRoll.Take(countToRoll).ToList();
                        
                        _logger.LogWarning("Force rolling out {Count} oldest completed movies to recover disk space", forcedRollOut.Count);
                        
                        forcedRollOut.ForEach(m =>
                        {
                            _logger.LogInformation("Force marking for cleanup: {MovieName}", m.Name);
                            m.LastKnownStatus = MovieStatus.RolledOut;
                        });
                        _data.Movies.UpdateRange(forcedRollOut);
                        
                        // Recalculate after forced rollout
                        currentAllocatedSpace = _data.Movies.Where(m => 
                            m.LastKnownStatus == MovieStatus.Complete ||
                            m.LastKnownStatus == MovieStatus.Stopped ||
                            m.LastKnownStatus == MovieStatus.PendingCheck ||
                            m.LastKnownStatus == MovieStatus.Checking ||
                            m.LastKnownStatus == MovieStatus.PendingDownload ||
                            m.LastKnownStatus == MovieStatus.Downloading ||
                            m.LastKnownStatus == MovieStatus.Enqueued)
                            .Sum(m => m.Size);
                        _logger.LogInformation("After forced rollout - Allocated space: {CurrentSpace} bytes / {MaxSpace} bytes", currentAllocatedSpace, _maxAllocatedSpace);
                    }
                    else
                    {
                        _logger.LogWarning("No completed movies available for forced rollout");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking disk space for forced rollout");
            }
            
            // NEW: Check actual filesystem space before enqueueing
            if (moviesToEnqueue.Count > 0)
            {
                var totalToDownload = moviesToEnqueue.Sum(m => m.Size);
                
                if (!HasEnoughDiskSpace(totalToDownload))
                {
                    _logger.LogWarning("Filesystem check failed. Skipping enqueue of {Count} movies to prevent 'no space' errors.", 
                        moviesToEnqueue.Count);
                    
                    // NEW: Try cleaning incomplete directory to recover space
                    if (_cleanIncompleteBeforeDownload)
                    {
                        _logger.LogInformation("Attempting to recover space by cleaning incomplete directory...");
                        CleanIncompleteDirectory();
                        
                        // Re-check after cleanup
                        if (!HasEnoughDiskSpace(totalToDownload))
                        {
                            _logger.LogWarning("Still insufficient disk space after incomplete cleanup. Aborting enqueue.");
                            await _data.SaveChangesAsync(cancellationToken);
                            return;
                        }
                    }
                    else
                    {
                        await _data.SaveChangesAsync(cancellationToken);
                        return;
                    }
                }
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
            
            await _data.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("CleanAndSyncMovies operation completed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during CleanAndSyncMovies operation");
            throw;
        }
    }

    private async Task GetNewMoviesAsync(CancellationToken cancellationToken)
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
                cancellationToken.ThrowIfCancellationRequested();
                
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
                        LastKnownStatus = MovieStatus.NotEnqueued,
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
                await _data.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("GetNewMovies completed - Added: {AddedCount}, Skipped: {SkippedCount}", addedCount, skippedCount);
            }
            else
            {
                _logger.LogInformation("No new movies found");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching new movies from YTS");
            throw;
        }
    }

    private async Task RetryFailedMoviesAsync(CancellationToken cancellationToken)
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
                await _data.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrying failed movies");
            throw;
        }
    }
}