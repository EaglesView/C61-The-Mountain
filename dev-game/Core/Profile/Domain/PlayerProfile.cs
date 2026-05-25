namespace Core.Profile.Domain;

public sealed class PlayerProfile
{
    public string UserId { get; }
    public string Username { get; private set; }
    public string SelectedHatId { get; private set; } = HatRegistry.NoneHatId;

    public PlayerProfile(string userId, string username, string? selectedHatId = null)
    {
        UserId = userId;
        Username = username;
        SelectedHatId = selectedHatId ?? HatRegistry.NoneHatId;
    }

    public void UpdateUsername(string username) => Username = username;
    public void UpdateHatId(string hatId) => SelectedHatId = hatId;
}
