namespace Core.Auth.Domain;

// template user
public sealed class User
{
    public string Id { get; }
    public string Email { get; }
    public string Username { get; }

    // Compte invité partagé&#160;: les guests utilisent des emails du pool
    // "invite{N}@outilsenligne.ca" (cf. Login.cs). Le serveur s'en sert pour
    // n'appliquer la règle d'unicité de session qu'aux invités — un compte
    // utilisateur normal reste libre de se connecter depuis plusieurs machines.
    public bool IsGuest =>
        Email.StartsWith("invite", System.StringComparison.OrdinalIgnoreCase)
        && Email.EndsWith("@outilsenligne.ca", System.StringComparison.OrdinalIgnoreCase);

    public User(string id, string email, string username)
    {
        Id = id ?? throw new System.ArgumentNullException(nameof(id));
        Email = email ?? throw new System.ArgumentNullException(nameof(email));
        Username = username ?? throw new System.ArgumentNullException(nameof(username));
    }
}
