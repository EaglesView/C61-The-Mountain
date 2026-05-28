using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Shared.Infrastructure;

public sealed class RealtimeDbClient
{
    private static readonly HttpClient Http = new();
    private readonly string _baseUrl;

    public RealtimeDbClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    private string BuildUrl(string path, string idToken, string? query = null)
    {
        var url = $"{_baseUrl}/{path.TrimStart('/')}.json?auth={Uri.EscapeDataString(idToken)}";
        if (!string.IsNullOrEmpty(query)) url += "&" + query;
        return url;
    }

    public async Task<string?> PushAsync(string path, object value, string idToken)
    {
        var content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        var response = await Http.PostAsync(BuildUrl(path, idToken), content);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
    }

    public async Task SetAsync(string path, object value, string idToken)
    {
        var content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        var response = await Http.PutAsync(BuildUrl(path, idToken), content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonDocument?> GetAsync(string path, string idToken, string? query = null)
    {
        var response = await Http.GetAsync(BuildUrl(path, idToken, query));
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    public async Task StreamAsync(
        string path,
        string idToken,
        string? query,
        Action<string, JsonElement> onEvent,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(path, idToken, query));
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;

            if (line.Length == 0)
            {
                if (eventName is not null && dataBuilder.Length > 0)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(dataBuilder.ToString());
                        onEvent(eventName, doc.RootElement.Clone());
                    }
                    catch (JsonException) { }
                }
                eventName = null;
                dataBuilder.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
                eventName = line.Substring(6).Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (dataBuilder.Length > 0) dataBuilder.Append('\n');
                dataBuilder.Append(line.Substring(5).TrimStart());
            }
        }
    }
}
