using System;
using System.Threading.Tasks;

namespace Core.Chat.Domain;

public interface IChatRepository
{
    event Action<ChatMessage> MessageReceived;
    Task SendAsync(string roomId, ChatMessage message);
    void Subscribe(string roomId);
    void Unsubscribe();
}
