using Digger.Data.Common.Enums;
using System.ComponentModel.DataAnnotations;

namespace Digger.Data.Entities;

public class Movie
{
    [Key]
    public long Id { get; set; }

    [Required]
    public string? Name { get; set; }

    [Required]
    public string? Path { get; set; }

    [Required]
    public string? TorrentUrl { get; set; }

    [Required]
    public DateTime? Timestamp { get; set; }

    public int DownloadAttempt { get; set; } = 1;

    public MovieStatus LastKnownStatus { get; set; }

    public long Size { get; set; }

    public DateTime PublishDate { get; set; }
}
