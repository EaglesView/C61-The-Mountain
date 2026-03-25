using System.Threading.Tasks;

namespace Core.Profile.Domain;

public interface IPlayerProfileRepository
{
    Task<PlayerProfile?> GetByUserIdAsync(string userId);
    Task SaveAsync(PlayerProfile profile);
}
