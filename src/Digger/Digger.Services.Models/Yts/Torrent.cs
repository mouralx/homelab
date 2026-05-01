namespace Digger.Services.Models.Yts;

public class Torrent
{
    public string url { get; set; }

    public string hash { get; set; }

    public string quality { get; set; }

    public string type { get; set; }

    public string is_repack { get; set; }

    public string video_codec { get; set; }

    public string bit_depth { get; set; }

    public string audio_channels { get; set; }

    public int seeds { get; set; }

    public int peers { get; set; }

    public string size { get; set; }

    public long size_bytes { get; set; }

    public string date_uploaded { get; set; }

    public int date_uploaded_unix { get; set; }
}
