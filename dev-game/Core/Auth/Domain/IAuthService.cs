using System.Threading.Tasks;

namespace Core.Auth.Domain;

// Domain pour les services d'auth.
// Si eventuellement on veut changer pour nimporte quoi dautre.
// donc permet de pas connaitre firebase ici
// donc basicaly template pour les providers
public interface IAuthService
{
    Task<AuthResult> SignInAsync(string email, string password);
    Task<AuthResult> SignUpAsync(string email, string password, string username);
    Task SignOutAsync();
    User? CurrentUser { get; }
    bool IsAuthenticated { get; }
    string? IdToken { get; }
}
