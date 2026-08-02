using Digger.Services.Models.Yts;

namespace Digger.Services.Contracts;

public interface IYtsService
{
    Task<ICollection<YtsMovieModel>> GetMoviesAsync();
}
