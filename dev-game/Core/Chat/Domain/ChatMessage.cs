namespace Core.Chat.Domain;

public sealed record ChatMessage(string UserId, string Username, string Text, long TimestampMs);
