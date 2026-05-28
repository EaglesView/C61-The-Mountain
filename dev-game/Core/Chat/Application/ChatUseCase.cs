using System;
using System.Threading.Tasks;
using Core.Chat.Domain;

namespace Core.Chat.Application;

public sealed class ChatUseCase
{
    private const int MaxLength = 500;
    private readonly IChatRepository _repository;

    public event Action<ChatMessage>? MessageReceived;

    public ChatUseCase(IChatRepository repository)
    {
        _repository = repository;
        _repository.MessageReceived += msg => MessageReceived?.Invoke(msg);
    }

    public Task SendAsync(string roomId, string userId, string username, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        var trimmed = text.Trim();
        if (trimmed.Length > MaxLength) trimmed = trimmed.Substring(0, MaxLength);
        var message = new ChatMessage(
            userId,
            username,
            trimmed,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return _repository.SendAsync(roomId, message);
    }

    public void Subscribe(string roomId) => _repository.Subscribe(roomId);
    public void Unsubscribe() => _repository.Unsubscribe();
}
