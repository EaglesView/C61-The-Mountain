using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Core.Chat.Domain;
using Core.Shared.Infrastructure;
using Godot;

namespace Core.Chat.Infrastructure;

public sealed class FirebaseChatRepository : IChatRepository
{
    private const int HistoryLimit = 50;
    private readonly RealtimeDbClient _client;
    private readonly Func<string> _getIdToken;
    private CancellationTokenSource? _streamCts;

    public event Action<ChatMessage>? MessageReceived;

    public FirebaseChatRepository(RealtimeDbClient client, Func<string> getIdToken)
    {
        _client = client;
        _getIdToken = getIdToken;
    }

    public Task SendAsync(string roomId, ChatMessage message)
    {
        var payload = new MessageDto
        {
            UserId = message.UserId,
            Username = message.Username,
            Text = message.Text,
            Ts = message.TimestampMs
        };
        return _client.PushAsync(MessagesPath(roomId), payload, _getIdToken());
    }

    public void Subscribe(string roomId)
    {
        Unsubscribe();
        _streamCts = new CancellationTokenSource();
        var ct = _streamCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _client.StreamAsync(
                    MessagesPath(roomId),
                    _getIdToken(),
                    $"orderBy=%22%24key%22&limitToLast={HistoryLimit}",
                    HandleEvent,
                    ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GD.PushError($"[Chat] Stream ended: {ex.Message}");
            }
        }, ct);
    }

    public void Unsubscribe()
    {
        if (_streamCts is null) return;
        _streamCts.Cancel();
        _streamCts.Dispose();
        _streamCts = null;
    }

    private void HandleEvent(string eventName, JsonElement data)
    {
        if (eventName != "put" && eventName != "patch") return;
        if (data.ValueKind != JsonValueKind.Object) return;
        if (!data.TryGetProperty("path", out var pathProp)) return;
        if (!data.TryGetProperty("data", out var dataProp)) return;
        if (dataProp.ValueKind == JsonValueKind.Null) return;

        var path = pathProp.GetString() ?? "/";
        if (path == "/")
        {
            if (dataProp.ValueKind != JsonValueKind.Object) return;
            var messages = new List<ChatMessage>();
            foreach (var prop in dataProp.EnumerateObject())
            {
                var msg = ParseMessage(prop.Value);
                if (msg is not null) messages.Add(msg);
            }
            foreach (var msg in messages.OrderBy(m => m.TimestampMs))
                Emit(msg);
        }
        else
        {
            var msg = ParseMessage(dataProp);
            if (msg is not null) Emit(msg);
        }
    }

    private static ChatMessage? ParseMessage(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var userId   = el.TryGetProperty("userId", out var u) ? u.GetString() ?? "" : "";
        var username = el.TryGetProperty("username", out var n) ? n.GetString() ?? "" : "";
        var text     = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        long ts = 0;
        if (el.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number)
            ts = tsEl.GetInt64();
        if (string.IsNullOrEmpty(text)) return null;
        return new ChatMessage(userId, username, text, ts);
    }

    private void Emit(ChatMessage msg)
    {
        Callable.From(() => MessageReceived?.Invoke(msg)).CallDeferred();
    }

    private static string MessagesPath(string roomId) => $"chat/rooms/{roomId}/messages";

    private sealed class MessageDto
    {
        [JsonPropertyName("userId")]   public string UserId   { get; set; } = "";
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("text")]     public string Text     { get; set; } = "";
        [JsonPropertyName("ts")]       public long   Ts       { get; set; }
    }
}
