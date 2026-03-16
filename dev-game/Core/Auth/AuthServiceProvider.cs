using Core.Auth.Application;
using Core.Auth.Domain;
using Core.Auth.Infrastructure;
using Config;

namespace Core.Auth;

public static class AuthServiceProvider
{
    private static AuthUseCase? _instance;
    public static AuthUseCase Instance
    {
        get
        {
            if (_instance is null)
            {
                // Le provider est hardcode ici, si jamais on va avec autre chose cest ici qu'on switch
                IAuthService service = new FirebaseAuthService(FirebaseConfig.ApiKey);

                _instance = new AuthUseCase(service);
            }
            return _instance;
        }
    }
    
    public static string GetCurrentToken() =>
        Instance.IdToken ?? throw new System.InvalidOperationException("User is not authenticated.");

    public static async System.Threading.Tasks.Task SignOutAsync()
    {
        await Instance.SignOutAsync();
        Reset();
    }

    public static void Reset() => _instance = null;
}
