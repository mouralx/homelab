using Digger.Data.Common.Enums;
using Digger.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Transmission.API.RPC;
using Transmission.API.RPC.Entity;

namespace Digger.Services.Implementations;

public class TransmissionService : ITransmissionService
{
    private readonly string? _transmissionUrl;

    private readonly bool _transmissionUseAuth;

    private readonly ILogger<TransmissionService> _logger;

    private readonly string? _transmissionUser;

    private readonly string? _transmissionPassword;

    private ITransmissionClient? _cachedClient;

    public TransmissionService(IConfiguration configuration, ILogger<TransmissionService> logger)
    {
        _transmissionUrl = configuration.GetRequiredSection("Transmission:ServerUrl").Value;
        _transmissionUseAuth = bool.Parse(configuration.GetRequiredSection("Transmission:UseAuth").Value);
        _transmissionUser = configuration.GetSection("Transmission:User").Value;
        _transmissionPassword = configuration.GetSection("Transmission:Password").Value;
        _logger = logger;
        _logger.LogInformation("TransmissionService initialized - URL: {Url}, UseAuth: {UseAuth}",
            _transmissionUrl, _transmissionUseAuth);
    }

    private ITransmissionClient GetTransmissionClient()
    {
        if (_cachedClient != null)
        {
            _logger.LogDebug("Reusing cached Transmission client");
            return _cachedClient;
        }

        _logger.LogInformation("Creating Transmission client for URL: {TransmissionUrl}, UseAuth: {UseAuth}", 
            _transmissionUrl, _transmissionUseAuth);

        _cachedClient = _transmissionUseAuth
            ? new Client(_transmissionUrl, null, _transmissionUser, _transmissionPassword)
            : new Client(_transmissionUrl);

        _logger.LogInformation("Transmission client created and cached successfully");
        return _cachedClient;
    }

    public void Download(string fileName, string downloadDirectory)
    {
        try
        {
            _logger.LogInformation("Transmission.Download - Starting torrent download - File: {FileName}, Directory: {DownloadDirectory}", 
                fileName, downloadDirectory);

            var startTime = DateTime.UtcNow;
            NewTorrent newTorrent = new NewTorrent
            {
                Filename = fileName,
                DownloadDirectory = downloadDirectory
            };

            _logger.LogInformation("Transmission.Download - Calling TorrentAdd with File: {FileName}, Directory: {DownloadDirectory}",
                newTorrent.Filename, newTorrent.DownloadDirectory);

            GetTransmissionClient().TorrentAdd(newTorrent);

            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation("Transmission.Download - Torrent successfully added in {Elapsed:F1}s - File: {FileName}",
                elapsed.TotalSeconds, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transmission.Download - Error adding torrent - File: {FileName}, Directory: {DownloadDirectory}", 
                fileName, downloadDirectory);
            throw;
        }
    }

    public MovieStatus GetStatus(string downloadDirectory)
    {
        try
        {
            _logger.LogInformation("Transmission.GetStatus - Getting torrent status for directory: {DownloadDirectory}", downloadDirectory);

            var startTime = DateTime.UtcNow;
            var client = GetTransmissionClient();
            var torrents = client.TorrentGet(TorrentFields.ALL_FIELDS);
            var elapsed = DateTime.UtcNow - startTime;

            var totalTorrents = torrents?.Torrents?.Length ?? 0;
            _logger.LogInformation("Transmission.GetStatus - Fetched {Count} torrents from Transmission in {Elapsed:F1}s",
                totalTorrents, elapsed.TotalSeconds);

            var torrent = torrents?.Torrents?.Where((TorrentInfo t) => t.DownloadDir == downloadDirectory)?.FirstOrDefault();

            if (torrent != null)
            {
                var rawStatus = torrent.Status;
                var movieStatus = (MovieStatus)rawStatus;
                var percentDone = torrent.PercentDone * 100;
                _logger.LogInformation("Transmission.GetStatus - Found torrent: Dir={DownloadDirectory}, RawStatus={RawStatus}({MovieStatus}), PercentDone={Percent:F1}%, Peers={Peers}, Seeds={Seeds}",
                    downloadDirectory, rawStatus, movieStatus, percentDone, torrent.PeersConnected, torrent.PeersSendingToUs);
                return movieStatus;
            }

            _logger.LogWarning("Transmission.GetStatus - No torrent found for directory: {DownloadDirectory}. Returning NotEnqueued(7).",
                downloadDirectory);
            return MovieStatus.NotEnqueued;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transmission.GetStatus - Error getting status for directory: {DownloadDirectory}", downloadDirectory);
            throw;
        }
    }

    public int DownloadsCount()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var client = GetTransmissionClient();
            var response = client.TorrentGet(TorrentFields.ALL_FIELDS);
            var elapsed = DateTime.UtcNow - startTime;

            var count = response?.Torrents?.Length ?? 0;

            var activeCount = response?.Torrents?.Count(t => t.Status == (int)MovieStatus.Downloading
                                                          || t.Status == (int)MovieStatus.PendingDownload
                                                          || t.Status == (int)MovieStatus.Checking
                                                          || t.Status == (int)MovieStatus.PendingCheck) ?? 0;

            var seedingCount = response?.Torrents?.Count(t => t.Status == (int)MovieStatus.Seeding
                                                           || t.Status == (int)MovieStatus.PendingSeed) ?? 0;

            var stoppedCount = response?.Torrents?.Count(t => t.Status == (int)MovieStatus.Stopped) ?? 0;

            _logger.LogInformation("Transmission.DownloadsCount - Total: {Total}, Active: {Active}, Seeding: {Seeding}, Stopped: {Stopped} (fetched in {Elapsed:F1}s)",
                count, activeCount, seedingCount, stoppedCount, elapsed.TotalSeconds);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transmission.DownloadsCount - Error getting downloads count");
            throw;
        }
    }

    public string[] ClenByStatuses(params MovieStatus[] statuses)
    {
        try
        {
            int[] convertedStatuses = statuses.Select((MovieStatus s) => (int)s).ToArray();
            var statusLabels = string.Join(", ", statuses.Select(s => $"{s}({(int)s})"));
            _logger.LogInformation("Transmission.ClenByStatuses - Cleaning torrents matching statuses: [{Statuses}]", statusLabels);

            var startTime = DateTime.UtcNow;
            ITransmissionClient client = GetTransmissionClient();
            var response = client.TorrentGet(TorrentFields.ALL_FIELDS);
            var fetchElapsed = DateTime.UtcNow - startTime;

            var totalTorrents = response?.Torrents?.Length ?? 0;
            if (totalTorrents == 0)
            {
                _logger.LogInformation("Transmission.ClenByStatuses - No torrents in Transmission (fetched in {Elapsed:F1}s)", fetchElapsed.TotalSeconds);
                return Array.Empty<string>();
            }

            IEnumerable<TorrentInfo> torrentInfos = response?.Torrents?.Where((TorrentInfo t) => convertedStatuses.Contains(t.Status));
            var matchedTorrents = torrentInfos?.ToList() ?? new List<TorrentInfo>();

            _logger.LogInformation("Transmission.ClenByStatuses - Total torrents: {Total}, Matching statuses: {Matched} (fetched in {Elapsed:F1}s)",
                totalTorrents, matchedTorrents.Count, fetchElapsed.TotalSeconds);

            if (matchedTorrents.Count == 0)
            {
                _logger.LogInformation("Transmission.ClenByStatuses - No torrents found with statuses: [{Statuses}]", statusLabels);
                return Array.Empty<string>();
            }

            // Log each matched torrent before removing
            foreach (var t in matchedTorrents)
            {
                var name = !string.IsNullOrEmpty(t.Name) ? t.Name : $"Torrent #{t.ID}";
                var pct = (t.PercentDone * 100).ToString("F1");
                _logger.LogInformation("Transmission.ClenByStatuses - Matched torrent: ID={Id}, Name="{Name}", Status={Status}, Progress={Percent}%, Dir={Dir}",
                    t.ID, name, t.Status, pct, t.DownloadDir);
            }

            IEnumerable<string> source = matchedTorrents.Select((TorrentInfo t) => t.DownloadDir);
            IEnumerable<int> ids = matchedTorrents.Select((TorrentInfo t) => t.ID);

            if (ids.Any())
            {
                var removeStart = DateTime.UtcNow;
                _logger.LogInformation("Transmission.ClenByStatuses - Removing {Count} torrents. IDs: [{TorrentIds}]",
                    ids.Count(), string.Join(", ", ids));
                client.TorrentRemove(ids.ToArray());
                var removeElapsed = DateTime.UtcNow - removeStart;
                _logger.LogInformation("Transmission.ClenByStatuses - Removed {Count} torrents in {Elapsed:F1}s",
                    ids.Count(), removeElapsed.TotalSeconds);
            }

            var directories = source.ToArray();
            _logger.LogInformation("Transmission.ClenByStatuses - Completed. Returned {Count} directory paths", directories.Length);
            return directories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transmission.ClenByStatuses - Error cleaning torrents");
            throw;
        }
    }

    public string[] GetPaths()
    {
        try
        {
            _logger.LogInformation("Transmission.GetPaths - Fetching all torrent download paths...");

            var startTime = DateTime.UtcNow;
            var client = GetTransmissionClient();
            var response = client.TorrentGet(TorrentFields.ALL_FIELDS);
            var elapsed = DateTime.UtcNow - startTime;

            var totalTorrents = response?.Torrents?.Length ?? 0;
            var paths = response?.Torrents?.Select((TorrentInfo t) => t.DownloadDir)?.ToArray() ?? Array.Empty<string>();

            _logger.LogInformation("Transmission.GetPaths - Found {Count} torrents with paths in {Elapsed:F1}s",
                totalTorrents, elapsed.TotalSeconds);

            if (paths.Length > 0)
            {
                _logger.LogInformation("Transmission.GetPaths - Torrent directories:");
                foreach (var path in paths)
                {
                    _logger.LogInformation("Transmission.GetPaths -   {Path}", path);
                }
            }

            return paths;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transmission.GetPaths - Error getting torrent paths");
            throw;
        }
    }
}
