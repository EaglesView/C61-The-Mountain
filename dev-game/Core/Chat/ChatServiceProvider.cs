using Core.Auth;
using Core.Chat.Application;
using Core.Chat.Domain;
using Core.Chat.Infrastructure;
using Core.Shared.Infrastructure;
using Config;

namespace Core.Chat;

public static class ChatServiceProvider
{
    private static ChatUseCase? _instance;

    public static ChatUseCase Instance
    {
        get
        {
            if (_instance is null)
            {
                var client = new RealtimeDbClient(FirebaseConfig.RtdbUrl);
                IChatRepository repo = new FirebaseChatRepository(
                    client, AuthServiceProvider.GetCurrentToken);
                _instance = new ChatUseCase(repo);
            }
            return _instance;
        }
    }

    public static void Reset() => _instance = null;
}
