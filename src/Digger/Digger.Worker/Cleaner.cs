using Digger.Data.Common.Enums;
using Digger.Data.Context;
using System.IO;

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

                var moviesToClean = new List<string>();

                // Clean RolledOut movies (existing behavior)
                var rolledOutMovies = _dbContext.Movies
                    .Where(m => m.LastKnownStatus == MovieStatus.RolledOut)
                    .Select(m => m.Path)
                    .ToList();

                _logger.LogInformation("Found {Count} rolled out movies", rolledOutMovies.Count);

                // NEW: Also clean Failed movies (they may have partial downloads)
                var failedMovies = _dbContext.Movies
                    .Where(m => m.LastKnownStatus == MovieStatus.Failed)
                    .Select(m => m.Path)
                    .ToList();

                _logger.LogInformation("Found {Count} failed movies to clean up", failedMovies.Count);

                // Combine both lists and extract directory names in memory (not via EF Core)
                moviesToClean = rolledOutMovies.Concat(failedMovies)
                    .Select(p => new DirectoryInfo(p).Name)
                    .Distinct()
                    .ToList();

                var movieDirectories = Directory.GetDirectories("/downloads/movies")
                    .Select(d => new DirectoryInfo(d).Name)
                    .ToList();

                _logger.LogInformation("Found {Count} movie directories on disk", movieDirectories.Count);

                var cleanedCount = 0;
                var toDelete = moviesToClean.Intersect(movieDirectories).ToList();
                foreach (var movie in toDelete)
                {
                    try
                    {
                        var path = Path.Combine("/downloads/movies", movie);
                        _logger.LogInformation("Deleting rolled out movie directory: {Path}", path);
                        Directory.Delete(path, true);
                        _logger.LogInformation("Movie directory deleted: {Path}", path);
                        cleanedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete movie directory: {Movie}", movie);
                    }
                }

                // NEW: Remove cleaned Failed movies from database to prevent re-cleaning
                // Materialize the failed paths in memory first, then filter EF results in-memory
                if (failedMovies.Count > 0)
                {
                    var failedDirNames = failedMovies
                        .Select(p => new DirectoryInfo(p).Name)
                        .ToHashSet();

                    var failedMoviesToDelete = _dbContext.Movies
                        .Where(m => m.LastKnownStatus == MovieStatus.Failed)
                        .ToList()  // Materialize before client-side filtering
                        .Where(m => failedDirNames.Contains(new DirectoryInfo(m.Path).Name))
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