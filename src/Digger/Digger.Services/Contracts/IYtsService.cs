using Digger.Services.Models.Yts;

namespace Digger.Services.Contracts;

public interface IYtsService
{
    ICollection<YtsMovieModel> GetMovies();
}
