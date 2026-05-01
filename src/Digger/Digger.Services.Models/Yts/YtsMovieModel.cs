namespace Digger.Services.Models.Yts;

using System;

public class YtsMovieModel
{
    public string? Id { get; set; }

    public string? TorrentUrl { get; set; }

    public string? Name { get; set; }

    public long Size { get; set; }

    public DateTime PublishDate { get; set; }
}
