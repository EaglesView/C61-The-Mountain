namespace Core.Auth.Domain;

// template pour frontend, retour du provider
public sealed class AuthResult
{
    public bool Success { get; }
    public string? UserId { get; }
    public string? Email { get; }
    public string? Username { get; }
    public string? Token { get; }
    public string? ErrorMessage { get; }

    private AuthResult(bool success, string? userId, string? email, string? username, string? token, string? errorMessage)
    {
        Success = success;
        UserId = userId;
        Email = email;
        Username = username;
        Token = token;
        ErrorMessage = errorMessage;
    }

    public static AuthResult Ok(string userId, string email, string username, string token)
        => new(true, userId, email, username, token, null);

    public static AuthResult Fail(string errorMessage)
        => new(false, null, null, null, null, errorMessage);
}
