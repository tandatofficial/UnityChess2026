using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessServer.Auth;

public sealed class SessionTokens
{
    private readonly byte[] _secret;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SessionTokens(IConfiguration configuration, bool allowEphemeralSecret = false)
    {
        _secret = GetSecret(configuration, allowEphemeralSecret);
    }

    public string Issue(UserRecord user)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new SessionPayload(user.Id, user.Username, DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds()),
            JsonOptions);
        var body = Base64UrlEncode(payload);
        return $"{body}.{Base64UrlEncode(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(body)))}";
    }

    public SessionPayload? Verify(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;
        try
        {
            var actual = Base64UrlDecode(parts[1]);
            var expected = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(parts[0]));
            if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)) return null;
            var payload = JsonSerializer.Deserialize<SessionPayload>(Base64UrlDecode(parts[0]), JsonOptions);
            return payload is not null && !string.IsNullOrEmpty(payload.Sub) &&
                   payload.Exp > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ? payload : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] GetSecret(IConfiguration configuration, bool allowEphemeralSecret)
    {
        var secret = configuration["SESSION_SECRET"];
        if (string.IsNullOrEmpty(secret) && allowEphemeralSecret)
            secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        if (string.IsNullOrEmpty(secret) || secret.Length < 32)
            throw new InvalidOperationException("SESSION_SECRET must contain at least 32 characters.");
        return Encoding.UTF8.GetBytes(secret);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}

public sealed record SessionPayload(
    [property: JsonPropertyName("sub")] string Sub,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("exp")] long Exp);

public sealed record UserRecord(
    string Id,
    string Username,
    string Email,
    string PasswordHash,
    string Salt,
    bool IsVerified,
    string? VerificationCode = null,
    long? VerificationCodeExpiry = null);

public sealed class SessionAuthenticationHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    SessionTokens sessions)
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..];
        if (string.IsNullOrEmpty(token) && Request.Path.StartsWithSegments("/hubs/game"))
            token = Request.Query["access_token"].ToString();
        var session = sessions.Verify(token);
        if (session is null) return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
        var identity = new System.Security.Claims.ClaimsIdentity(
        [
            new(System.Security.Claims.ClaimTypes.NameIdentifier, session.Sub),
            new(System.Security.Claims.ClaimTypes.Name, session.Name),
            new("name", session.Name)
        ], Scheme.Name);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}
