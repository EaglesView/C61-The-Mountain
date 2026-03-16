using System;
using System.Threading.Tasks;
using Core.Profile.Domain;
using Core.Shared.Infrastructure;

namespace Core.Profile.Infrastructure;

public sealed class FirestorePlayerProfileRepository : IPlayerProfileRepository
{
    private const string Collection = "profiles";
    private readonly FirestoreClient _client;
    private readonly Func<string> _getIdToken;

    public FirestorePlayerProfileRepository(FirestoreClient client, Func<string> getIdToken)
    {
        _client = client;
        _getIdToken = getIdToken;
    }

    public async Task<PlayerProfile?> GetByUserIdAsync(string userId)
    {
        var doc = await _client.GetDocumentAsync(Collection, userId, _getIdToken());
        if (doc is null) return null;

        var fields = doc.RootElement.GetProperty("fields");
        return new PlayerProfile(
            userId,
            fields.GetProperty("username").GetProperty("stringValue").GetString()!
        );
    }

    public async Task SaveAsync(PlayerProfile profile)
    {
        var fields = new
        {
            username = new { stringValue = profile.Username }
        };
        await _client.SetDocumentAsync(Collection, profile.UserId, fields, _getIdToken());
    }
}
