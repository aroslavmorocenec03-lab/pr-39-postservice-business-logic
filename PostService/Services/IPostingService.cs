using PostService.Models;

namespace PostService.Services;

public interface IPostingService
{
    Posting Create(Posting newPosting);
    List<Posting> GetAll();
    Posting? Find(int postingId);
    Posting? Update(int postingId, Posting posting);
    int Delete(int postingId);
}
