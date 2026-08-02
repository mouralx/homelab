using Digger.Data.Common.Enums;
using Digger.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Transmission.API.RPC;
using Transmission.API.RPC.Entity;

namespace Digger.Services.Implementations;

public class TransmissionService : ITransmissionService
{
    private readonly ITransmissionClient _client;
    private readonly ILogger<TransmissionService> _logger;

    public TransmissionService(IConfiguration configuration, ILogger<TransmissionService> logger)
    {
        _logger = logger;
        
        var transmissionUrl = configuration.GetRequiredSection("Transmission:ServerUrl").Value;
        var transmissionUseAuth = bool.Parse(configuration.GetRequiredSection("Transmission:UseAuth").Value);
        
        _logger.LogDebug("Creating Transmission client for URL: {TransmissionUrl}, UseAuth: {UseAuth}", 
            transmissionUrl, transmissionUseAuth);
        
        if (transmissionUseAuth)
        {
            var user = configuration.GetSection("Transmission:User").Value;
            var password = configuration.GetSection("Transmission:Password").Value;
            _logger.LogDebug("Using authenticated connection with username: {Username}", user);
            _client = new Client(transmissionUrl, null, user, password);
        }
        else
        {
            _logger.LogDebug("Using unauthenticated connection");
            _client = new Client(transmissionUrl);
        }
    }

    public void Download(string fileName, string downloadDirectory)
    {
        try
        {
            _logger.LogInformation("Starting torrent download - File: {FileName}, Directory: {DownloadDirectory}", 
                fileName, downloadDirectory);
            
            NewTorrent newTorrent = new NewTorrent
            {
                Filename = fileName,
                DownloadDirectory = downloadDirectory
            };
            
            _client.TorrentAdd(newTorrent);
            _logger.LogInformation("Torrent successfully added to Transmission");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding torrent to Transmission - File: {FileName}", fileName);
            throw;
        }
    }

    public MovieStatus GetStatus(string downloadDirectory)
    {
        try
        {
            _logger.LogDebug("Getting torrent status for directory: {DownloadDirectory}", downloadDirectory);
            
            var torrents = _client.TorrentGet(TorrentFields.ALL_FIELDS);
            var torrent = torrents?.Torrents?.Where((TorrentInfo t) => t.DownloadDir == downloadDirectory)?.FirstOrDefault();
            
            var status = (MovieStatus)(torrent?.Status ?? 7);
            _logger.LogDebug("Torrent status: {Status} for directory: {DownloadDirectory}", status, downloadDirectory);
            
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting torrent status");
            throw;
        }
    }

    public int DownloadsCount()
    {
        try
        {
            var count = _client.TorrentGet(TorrentFields.ALL_FIELDS).Torrents.Count();
            _logger.LogDebug("Current active downloads count: {Count}", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting downloads count");
            throw;
        }
    }

    public string[] CleanByStatuses(params MovieStatus[] statuses)
    {
        try
        {
            _logger.LogInformation("Cleaning torrents by statuses: {Statuses}", 
                string.Join(", ", statuses.Select(s => $"{s}({(int)s})")));
            
            int[] convertedStatuses = statuses.Select((MovieStatus s) => (int)s).ToArray();
            IEnumerable<TorrentInfo> torrentInfos = _client.TorrentGet(TorrentFields.ALL_FIELDS)?.Torrents?.Where((TorrentInfo t) => convertedStatuses.Contains(t.Status));
            
            if (torrentInfos != null && torrentInfos.Any())
            {
                _logger.LogInformation("Found {Count} torrents to clean", torrentInfos.Count());
                
                IEnumerable<string> source = torrentInfos.Select((TorrentInfo t) => t.DownloadDir);
                IEnumerable<int> ids = torrentInfos.Select((TorrentInfo t) => t.ID);
                
                if (ids?.Any() ?? false)
                {
                    _logger.LogInformation("Removing {Count} torrents from Transmission. IDs: {TorrentIds}", 
                        ids.Count(), string.Join(", ", ids));
                    _client.TorrentRemove(ids.ToArray());
                }
                
                var directories = source.ToArray();
                _logger.LogInformation("Cleaned torrent directories: {Directories}", 
                    string.Join(", ", directories));
                return directories;
            }
            
            _logger.LogInformation("No torrents found with specified statuses");
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning torrents by status");
            throw;
        }
    }

    public string[] GetPaths()
    {
        try
        {
            _logger.LogDebug("Getting all torrent download paths...");
            
            var paths = _client.TorrentGet(TorrentFields.ALL_FIELDS)?.Torrents?.Select((TorrentInfo t) => t.DownloadDir)?.ToArray() ?? Array.Empty<string>();
            
            _logger.LogInformation("Found {Count} torrent paths", paths.Length);
            if (paths.Length > 0)
            {
                _logger.LogDebug("Torrent paths: {Paths}", string.Join(", ", paths));
            }
            
            return paths;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting torrent paths");
            throw;
        }
    }
}
