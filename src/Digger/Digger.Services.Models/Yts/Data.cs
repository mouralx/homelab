
namespace Digger.Services.Models.Yts;

public class Data
{
    public int movie_count { get; set; }

    public int limit { get; set; }

    public int page_number { get; set; }

    public Movie[] movies { get; set; }
}
