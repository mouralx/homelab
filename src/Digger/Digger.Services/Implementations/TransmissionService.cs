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
        if (_transmissionUseAuth)
        {
            return new Client(_transmissionUrl, null, _transmissionUser, _transmissionPassword);
        }
        return new Client(_transmissionUrl);
    }

    public void Download(string fileName, string downloadDirectory)
    {
        NewTorrent newTorrent = new NewTorrent
        {
            Filename = fileName,
            DownloadDirectory = downloadDirectory
        };
        GetTransmissionClient().TorrentAdd(newTorrent);
    }

    public MovieStatus GetStatus(string downloadDirectory)
    {
        return (MovieStatus)((GetTransmissionClient().TorrentGet(TorrentFields.ALL_FIELDS)?.Torrents?.Where((TorrentInfo t) => t.DownloadDir == downloadDirectory)?.FirstOrDefault())?.Status ?? 7);
    }

    public int DownloadsCount()
    {
        return GetTransmissionClient().TorrentGet(TorrentFields.ALL_FIELDS).Torrents.Count();
    }

    public string[] ClenByStatuses(params MovieStatus[] statuses)
    {
        ITransmissionClient client = GetTransmissionClient();
        int[] convertedStatuses = statuses.Select((MovieStatus s) => (int)s).ToArray();
        IEnumerable<TorrentInfo> torrentInfos = client.TorrentGet(TorrentFields.ALL_FIELDS)?.Torrents?.Where((TorrentInfo t) => convertedStatuses.Contains(t.Status));
        if (torrentInfos != null && torrentInfos.Any())
        {
            IEnumerable<string> source = torrentInfos.Select((TorrentInfo t) => t.DownloadDir);
            IEnumerable<int> ids = torrentInfos.Select((TorrentInfo t) => t.ID);
            if (ids?.Any() ?? false)
            {
                client.TorrentRemove(ids.ToArray());
            }
            return source.ToArray();
        }
        return Array.Empty<string>();
    }

    public string[] GetPaths()
    {
        return GetTransmissionClient().TorrentGet(TorrentFields.ALL_FIELDS)?.Torrents?.Select((TorrentInfo t) => t.DownloadDir)?.ToArray() ?? Array.Empty<string>();
    }
}
