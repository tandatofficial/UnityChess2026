using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessServer.Auth;

public sealed class SupabaseUsers(IHttpClientFactory clients, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string? _url = configuration["SUPABASE_URL"]?.TrimEnd('/');
    private readonly string? _key = configuration["SUPABASE_KEY"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_key);

    public async Task<UserRecord?> FindByLoginAsync(string login, CancellationToken cancellationToken)
    {
        var field = login.Contains('@') ? "email" : "username";
        var escaped = login.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var rows = await SendAsync<List<SupabaseUser>>(
            HttpMethod.Get, $"game_users?select=*&{field}=ilike.{Uri.EscapeDataString(escaped)}", null, cancellationToken);
        return rows?.FirstOrDefault()?.ToRecord();
    }

    public async Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var rows = await SendAsync<List<SupabaseUser>>(
            HttpMethod.Get, $"game_users?select=*&email=ilike.{Uri.EscapeDataString(email)}", null, cancellationToken);
        return rows?.FirstOrDefault()?.ToRecord();
    }

    public async Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var rows = await SendAsync<List<SupabaseUser>>(
            HttpMethod.Get, $"game_users?select=*&username=ilike.{Uri.EscapeDataString(username)}", null, cancellationToken);
        return rows?.FirstOrDefault()?.ToRecord();
    }

    public async Task<bool> InsertAsync(UserRecord user, CancellationToken cancellationToken)
    {
        var row = new SupabaseUser
        {
            Username = user.Username,
            Email = user.Email,
            PasswordHash = user.PasswordHash,
            Salt = user.Salt,
            IsVerified = false,
            VerificationCode = user.VerificationCode,
            VerificationCodeExpiry = user.VerificationCodeExpiry
        };
        await SendAsync<JsonElement>(HttpMethod.Post, "game_users", row, cancellationToken, "return=minimal");
        return true;
    }

    public async Task<bool> MarkVerifiedAsync(string id, CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement>(HttpMethod.Patch,
            $"game_users?id=eq.{Uri.EscapeDataString(id)}",
            new { is_verified = true, verification_code = (string?)null, verification_code_expiry = (long?)null },
            cancellationToken, "return=minimal");
        return true;
    }

    public async Task DeleteUnverifiedByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement>(HttpMethod.Delete,
            $"game_users?email=eq.{Uri.EscapeDataString(email)}&is_verified=eq.false",
            null, cancellationToken, "return=minimal");
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, string? prefer = null)
    {
        if (!IsConfigured) throw new InvalidOperationException("Supabase is not configured.");
        using var request = new HttpRequestMessage(method, $"{_url}/rest/v1/{path}");
        request.Headers.Add("apikey", _key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        if (prefer is not null) request.Headers.Add("Prefer", prefer);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new SupabaseException(response.StatusCode, details);
        }
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0) return default;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private sealed class SupabaseUser
    {
        [JsonPropertyName("id")] public JsonElement Id { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("password_hash")] public string PasswordHash { get; set; } = "";
        [JsonPropertyName("salt")] public string Salt { get; set; } = "";
        [JsonPropertyName("is_verified")] public bool IsVerified { get; set; }
        [JsonPropertyName("verification_code")] public string? VerificationCode { get; set; }
        [JsonPropertyName("verification_code_expiry")] public long? VerificationCodeExpiry { get; set; }

        public UserRecord ToRecord()
        {
            var id = Id.ValueKind switch
            {
                JsonValueKind.String => Id.GetString(),
                JsonValueKind.Number => Id.GetRawText(),
                _ => throw new JsonException("Supabase user id must be a string or number.")
            };
            return new UserRecord(id ?? "", Username, Email, PasswordHash, Salt, IsVerified, VerificationCode, VerificationCodeExpiry);
        }
    }
}

public sealed class SupabaseException(HttpStatusCode statusCode, string details) : Exception(details)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
