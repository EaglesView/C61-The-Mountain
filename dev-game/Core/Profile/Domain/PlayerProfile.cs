namespace Core.Profile.Domain;

public sealed class PlayerProfile
{
    public string UserId { get; }
    public string Username { get; private set; }

    public PlayerProfile(string userId, string username )
    {
        UserId = userId;
        Username = username;
    }

    public void UpdateUsername(string username) => Username = username;
}
