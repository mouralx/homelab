namespace Digger.Services.Models.Yts;

public class YtsResponse
{
    public string status { get; set; }

    public string status_message { get; set; }

    public Data data { get; set; }

    public Meta meta { get; set; }
}
