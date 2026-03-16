using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core.Shared.Infrastructure;

// pour le save eventuellement aussi de la partie
public sealed class FirestoreClient
{
    private static readonly HttpClient Http = new();
    private readonly string _projectId;

    public FirestoreClient(string projectId)
    {
        _projectId = projectId;
    }

    private string BaseUrl =>
        $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";

    public async Task<JsonDocument?> GetDocumentAsync(
        string collection, string documentId, string idToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/{collection}/{documentId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    public async Task SetDocumentAsync(
        string collection, string documentId,
        object fields, string idToken)
    {
        var body = JsonSerializer.Serialize(new { fields });
        var request = new HttpRequestMessage(HttpMethod.Patch,
            $"{BaseUrl}/{collection}/{documentId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteDocumentAsync(
        string collection, string documentId, string idToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            $"{BaseUrl}/{collection}/{documentId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
