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

    public TransmissionService(IConfiguration configuration, ILogger<TransmissionService> logger)
    {
        _transmissionUrl = configuration.GetRequiredSection("Transmission:ServerUrl").Value;
        _transmissionUseAuth = bool.Parse(configuration.GetRequiredSection("Transmission:UseAuth").Value);
        _transmissionUser = configuration.GetSection("Transmission:User").Value;
        _transmissionPassword = configuration.GetSection("Transmission:Password").Value;
        _logger = logger;
    }

    private ITransmissionClient GetTransmissionClient()
    {
        try
        {
            _logger.LogDebug("Creating Transmission client for URL: {TransmissionUrl}, UseAuth: {UseAuth}", 
                _transmissionUrl, _transmissionUseAuth);
            
            if (_transmissionUseAuth)
            {
                _logger.LogDebug("Using authenticated connection with username: {Username}", _transmissionUser);
                return new Client(_transmissionUrl, null, _transmissionUser, _transmissionPassword);
            }
            
            _logger.LogDebug("Using unauthenticated connection");
            return new Client(_transmissionUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Transmission client");
            throw;
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
            
            GetTransmissionClient().TorrentAdd(newTorrent);
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
            
            var client = GetTransmissionClient();
            var torrents = client.TorrentGet(TorrentFields.ALL_FIELDS);
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
            var client = GetTransmissionClient();
            var count = client.TorrentGet(TorrentFields.ALL_FIELDS).Torrents.Count();
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
            
            ITransmissionClient client = GetTransmissionClient();
            int[] convertedStatuses = statuses.Select((MovieStatus s) => (int)s).ToArray();
            IEnumerable<TorrentInfo> torrentInfos = client.TorrentGet(TorrentFields.ALL_FIELDS)?.Torrents?.Where((TorrentInfo t) => convertedStatuses.Contains(t.Status));
            
            if (torrentInfos != null && torrentInfos.Any())
            {
                _logger.LogInformation("Found {Count} torrents to clean", torrentInfos.Count());
                
                IEnumerable<string> source = torrentInfos.Select((TorrentInfo t) => t.DownloadDir);
                IEnumerable<int> ids = torrentInfos.Select((TorrentInfo t) => t.ID);
                
                if (ids?.Any() ?? false)
                {
                    _logger.LogInformation("Removing {Count} torrents from Transmission. IDs: {TorrentIds}",
                        ids.Count(), string.Join(", ", ids));
                    client.TorrentRemove(ids.ToArray());
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
            
            var client = GetTransmissionClient();
            var paths = client.TorrentGet(TorrentFields.ALL_FIELDS)?.Torrents?.Select((TorrentInfo t) => t.DownloadDir)?.ToArray() ?? Array.Empty<string>();
            
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
