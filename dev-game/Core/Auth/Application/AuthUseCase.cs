using System.Threading.Tasks;
using Core.Auth.Domain;

namespace Core.Auth.Application;

// verif et call domain
public sealed class AuthUseCase
{
    private readonly IAuthService _authService;
    public AuthUseCase(IAuthService authService)
    {
        _authService = authService;
    }

    public Task<AuthResult> SignInAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Task.FromResult(AuthResult.Fail("Email is required."));

        if (string.IsNullOrWhiteSpace(password))
            return Task.FromResult(AuthResult.Fail("Password is required."));

        return _authService.SignInAsync(email.Trim(), password);
    }

    public Task<AuthResult> SignUpAsync(string email, string password, string username)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Task.FromResult(AuthResult.Fail("Email is required."));

        if (string.IsNullOrWhiteSpace(password))
            return Task.FromResult(AuthResult.Fail("Password is required."));

        if (string.IsNullOrWhiteSpace(username))
            return Task.FromResult(AuthResult.Fail("Username is required."));

        if (username.Trim().Length < 2)
            return Task.FromResult(AuthResult.Fail("Username must be at least 2 characters."));

        if (password.Length < 6)
            return Task.FromResult(AuthResult.Fail("Password must be at least 6 characters."));

        return _authService.SignUpAsync(email.Trim(), password, username.Trim());
    }

    public Task SignOutAsync() => _authService.SignOutAsync();
    public User? CurrentUser => _authService.CurrentUser;
    public bool IsAuthenticated => _authService.IsAuthenticated;
    public string? IdToken => _authService.IdToken;
}
