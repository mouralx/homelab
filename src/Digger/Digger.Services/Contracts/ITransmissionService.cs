using Digger.Data.Common.Enums;

namespace Digger.Services.Contracts;

public interface ITransmissionService
{
    void Download(string torrentUrl, string downloadPath);

    MovieStatus GetStatus(string downloadDirectory);

    string[] ClenByStatuses(params MovieStatus[] statuses);

    int DownloadsCount();

    string[] GetPaths();
}
