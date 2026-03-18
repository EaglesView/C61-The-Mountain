using Core.Auth;
using Core.Profile.Application;
using Core.Profile.Domain;
using Core.Profile.Infrastructure;
using Core.Shared.Infrastructure;
using Config;

namespace Core.Profile;

public static class ProfileServiceProvider
{
    private static PlayerProfileUseCase? _instance;

    public static PlayerProfileUseCase Instance
    {
        get
        {
            if (_instance is null)
            {
                // pour db
                var client = new FirestoreClient(FirebaseConfig.ProjectId);
                IPlayerProfileRepository repo = new FirestorePlayerProfileRepository(
                    client, AuthServiceProvider.GetCurrentToken);
                _instance = new PlayerProfileUseCase(repo);
            }
            return _instance;
        }
    }

    public static void Reset() => _instance = null;
}
