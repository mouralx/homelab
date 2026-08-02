using Digger.Data.Common.Enums;
using Digger.Data.Context;

public class Cleaner : BackgroundService
{
    private readonly ILogger<Cleaner> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public Cleaner(ILogger<Cleaner> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Cleaner running at: {Time}", DateTimeOffset.Now);

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DiggerContext>();

                var rolledOutMovies = dbContext.Movies.Where(m => m.LastKnownStatus == MovieStatus.RolledOut).Select(m => new DirectoryInfo(m.Path).Name).ToList();

                _logger.LogInformation("Found {Count} rolled out movies", rolledOutMovies.Count);

                var movieDirectories = Directory.GetDirectories("/downloads/movies").Select(d => new DirectoryInfo(d).Name).ToList();

                _logger.LogInformation("Found {Count} movie directories", movieDirectories.Count);

                rolledOutMovies.Intersect(movieDirectories).ToList().ForEach(m =>
                {
                    _logger.LogInformation("Rolling out movie: {Movie}", m);

                    var path = Path.Combine("/downloads/movies", m);

                    _logger.LogInformation("Deleting rolled out movie: {Path}", path);
                    
                    Directory.Delete(path, true);

                    _logger.LogInformation("Rolled out movie deleted: {Path}", path);
                });

                _logger.LogInformation("Cleaner finished at: {Time}", DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while cleaning rolled out movies");
            }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}