using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Core.Auth.Domain;

namespace Core.Auth.Infrastructure;

// implementatoin firebase mostly boilerplate code, on pourrait faire supabase aussi pour montrer lutiliter de larchitecture
public sealed class FirebaseAuthService : IAuthService
{
    private static readonly HttpClient Http = new();
    private const string SignInUrl   = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=";
    private const string SignUpUrl   = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=";
    private const string UpdateUrl   = "https://identitytoolkit.googleapis.com/v1/accounts:update?key=";
    private readonly string _apiKey;
    private User? _currentUser;
    private string? _idToken;

    public FirebaseAuthService(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    }

    public User? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser is not null;
    public string? IdToken => _idToken;

    public Task<AuthResult> SignInAsync(string email, string password)
        => RequestAsync(SignInUrl + _apiKey, email, password);

    public async Task<AuthResult> SignUpAsync(string email, string password, string username)
    {
        var result = await RequestAsync(SignUpUrl + _apiKey, email, password);
        if (!result.Success)
            return result;

        var updateResult = await UpdateDisplayNameAsync(result.Token!, username);
        if (!updateResult)
            return AuthResult.Fail("Account created but failed to set username.");

        _currentUser = new User(result.UserId!, result.Email!, username);
        return AuthResult.Ok(result.UserId!, result.Email!, username, result.Token!);
    }

    public Task SignOutAsync()
    {
        _currentUser = null;
        _idToken = null;
        return Task.CompletedTask;
    }

    private async Task<AuthResult> RequestAsync(string url, string email, string password)
    {
        var payload = new FirebaseAuthRequest
        {
            Email = email,
            Password = password,
            ReturnSecureToken = true
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.PostAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = JsonSerializer.Deserialize<FirebaseErrorWrapper>(body);
                var message = errorResponse?.Error?.Message ?? "Unknown error";
                return AuthResult.Fail(MapFirebaseError(message));
            }

            var result = JsonSerializer.Deserialize<FirebaseAuthResponse>(body);
            if (result is null)
                return AuthResult.Fail("Failed to parse Firebase response.");

            _idToken = result.IdToken;
            // si displayname vide, fallback sur email
            var username = string.IsNullOrEmpty(result.DisplayName) ? result.Email : result.DisplayName;
            _currentUser = new User(result.LocalId, result.Email, username);

            return AuthResult.Ok(result.LocalId, result.Email, username, result.IdToken);
        }
        catch (HttpRequestException ex)
        {
            return AuthResult.Fail($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return AuthResult.Fail($"Unexpected error: {ex.Message}");
        }
    }

    private static string MapFirebaseError(string firebaseCode) => firebaseCode switch
    {
        "EMAIL_NOT_FOUND"             => "No account found with this email.",
        "INVALID_PASSWORD"            => "Incorrect password.",
        "USER_DISABLED"               => "This account has been disabled.",
        "EMAIL_EXISTS"                => "An account with this email already exists.",
        "INVALID_EMAIL"               => "Invalid email address.",
        "WEAK_PASSWORD"               => "Password is too weak.",
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many attempts. Please try again later.",
        "INVALID_LOGIN_CREDENTIALS"   => "Invalid email or password.",
        _                             => firebaseCode
    };
    private async Task<bool> UpdateDisplayNameAsync(string idToken, string displayName)
    {
        var payload = new FirebaseUpdateRequest
        {
            IdToken = idToken,
            DisplayName = displayName,
            ReturnSecureToken = true
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.PostAsync(UpdateUrl + _apiKey, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private sealed class FirebaseAuthRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("returnSecureToken")]
        public bool ReturnSecureToken { get; set; }
    }

    private sealed class FirebaseAuthResponse
    {
        [JsonPropertyName("localId")]
        public string LocalId { get; set; } = "";

        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = "";

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = "";

        [JsonPropertyName("expiresIn")]
        public string ExpiresIn { get; set; } = "";
    }

    private sealed class FirebaseErrorWrapper
    {
        [JsonPropertyName("error")]
        public FirebaseErrorDetail? Error { get; set; }
    }

    private sealed class FirebaseErrorDetail
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("code")]
        public int Code { get; set; }
    }
    
    private sealed class FirebaseUpdateRequest
    {
        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = "";

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("returnSecureToken")]
        public bool ReturnSecureToken { get; set; }
    }
}
