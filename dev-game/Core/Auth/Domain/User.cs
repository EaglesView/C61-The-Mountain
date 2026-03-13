namespace Core.Auth.Domain;

// template user
public sealed class User
{
    public string Id { get; }
    public string Email { get; }
    public string Username { get; }

    public User(string id, string email, string username)
    {
        Id = id ?? throw new System.ArgumentNullException(nameof(id));
        Email = email ?? throw new System.ArgumentNullException(nameof(email));
        Username = username ?? throw new System.ArgumentNullException(nameof(username));
    }
}
