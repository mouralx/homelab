using Digger.Data.Common.Enums;
using Digger.Data.Context;

public class Cleaner : BackgroundService
{
    private readonly ILogger<Cleaner> _logger;
    private readonly DiggerContext _dbContext;

    public Cleaner(ILogger<Cleaner> logger, DiggerContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Cleaner running at: {Time}", DateTimeOffset.Now);

                // Clean RolledOut movies (existing behavior)
                var rolledOutMovies = _dbContext.Movies
                    .Where(m => m.LastKnownStatus == MovieStatus.RolledOut)
                    .Select(m => new DirectoryInfo(m.Path).Name)
                    .ToList();

                _logger.LogInformation("Found {Count} rolled out movies", rolledOutMovies.Count);

                // NEW: Also clean Failed movies (they may have partial downloads)
                var failedMovies = _dbContext.Movies
                    .Where(m => m.LastKnownStatus == MovieStatus.Failed)
                    .Select(m => new DirectoryInfo(m.Path).Name)
                    .ToList();

                _logger.LogInformation("Found {Count} failed movies to clean up", failedMovies.Count);

                // Combine both lists
                var moviesToClean = rolledOutMovies.Concat(failedMovies).Distinct().ToList();

                var movieDirectories = Directory.GetDirectories("/downloads/movies")
                    .Select(d => new DirectoryInfo(d).Name)
                    .ToList();

                _logger.LogInformation("Found {Count} movie directories on disk", movieDirectories.Count);

                var cleanedCount = 0;
                moviesToClean.Intersect(movieDirectories).ToList().ForEach(m =>
                {
                    try
                    {
                        _logger.LogInformation("Cleaning up movie directory: {Movie}", m);

                        var path = Path.Combine("/downloads/movies", m);

                        _logger.LogInformation("Deleting movie directory: {Path}", path);
                        
                        Directory.Delete(path, true);

                        _logger.LogInformation("Movie directory deleted: {Path}", path);
                        cleanedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete movie directory: {Movie}", m);
                    }
                });

                // NEW: Remove cleaned Failed movies from database to prevent re-cleaning
                if (failedMovies.Count > 0)
                {
                    var failedMoviesToDelete = _dbContext.Movies
                        .Where(m => m.LastKnownStatus == MovieStatus.Failed && 
                                   failedMovies.Contains(new DirectoryInfo(m.Path).Name))
                        .ToList();

                    if (failedMoviesToDelete.Count > 0)
                    {
                        _logger.LogInformation("Removing {Count} cleaned failed movies from database", failedMoviesToDelete.Count);
                        _dbContext.Movies.RemoveRange(failedMoviesToDelete);
                        await _dbContext.SaveChangesAsync(stoppingToken);
                    }
                }

                _logger.LogInformation("Cleaner finished at: {Time}. Cleaned {Count} directories.", DateTimeOffset.Now, cleanedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while cleaning rolled out movies");
            }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}