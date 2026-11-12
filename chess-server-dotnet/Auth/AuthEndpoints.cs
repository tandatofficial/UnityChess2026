using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChessServer.Auth;

public sealed record RegisterRequest(string? Username, string? Email, string? Password);
public sealed record VerifyRequest(string? Email, string? Code);
public sealed record LoginRequest(string? UsernameOrEmail, string? Password);

public static partial class AuthEndpoints
{
    [GeneratedRegex("^[a-zA-Z0-9_]{3,32}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[^\\s@,%()]+@[^\\s@,%()]+\\.[^\\s@,%()]+$")]
    private static partial Regex EmailPattern();

    public static async Task<IResult> Register(RegisterRequest request, SupabaseUsers users, EmailSender email, CancellationToken cancellationToken)
    {
        if (!users.IsConfigured) return Error(503, "Database service is not configured.");
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || request.Password is null)
            return Error(400, "Missing required fields (username, email, password).");
        if (request.Username.Length > 256 || request.Email.Length > 256 || request.Password.Length > 256 ||
            !UsernamePattern().IsMatch(request.Username) || !EmailPattern().IsMatch(request.Email) || request.Password.Length < 8)
            return Error(400, "Use a 3-32 character username (letters, digits, underscore), valid email and password of at least 8 characters.");

        try
        {
            if (await users.FindByEmailAsync(request.Email, cancellationToken) is not null)
                return Error(400, "Email is already registered.");
            if (await users.FindByUsernameAsync(request.Username, cancellationToken) is not null)
                return Error(400, "Username is already taken.");

            var salt = RandomNumberGenerator.GetBytes(16);
            var user = new UserRecord("", request.Username, request.Email,
                "pbkdf2-sha256:600000:" + Convert.ToHexString(Pbkdf2(request.Password, salt, 600_000)).ToLowerInvariant(),
                Convert.ToHexString(salt).ToLowerInvariant(), false, RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(),
                DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds());
            await users.InsertAsync(user, cancellationToken);
            try
            {
                await email.SendVerificationAsync(request.Email, user.VerificationCode!, cancellationToken);
            }
            catch
            {
                try { await users.DeleteUnverifiedByEmailAsync(request.Email, cancellationToken); } catch { }
                return Error(503, "Verification email could not be sent. Please try again later.");
            }
            return Results.Ok(new { message = "Registration successful. Please check your email for the verification code." });
        }
        catch (SupabaseException)
        {
            return Error(500, "Database query failed.");
        }
        catch
        {
            return Error(500, "Internal server error.");
        }
    }

    public static async Task<IResult> Verify(VerifyRequest request, SupabaseUsers users, CancellationToken cancellationToken)
    {
        if (!users.IsConfigured) return Error(503, "Database service is not configured.");
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code) ||
            request.Email.Length > 256 || request.Code.Length > 256)
            return Error(400, "Missing required fields (email, code).");
        try
        {
            var user = await users.FindByEmailAsync(request.Email, cancellationToken);
            if (user is null) return Error(400, "Account not found.");
            if (user.IsVerified) return Error(400, "Account is already verified.");
            if (user.VerificationCode != request.Code) return Error(400, "Invalid verification code.");
            if (user.VerificationCodeExpiry is null || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > user.VerificationCodeExpiry)
                return Error(400, "Verification code has expired.");
            await users.MarkVerifiedAsync(user.Id, cancellationToken);
            return Results.Ok(new { message = "Email verification successful. You can now log in." });
        }
        catch (SupabaseException)
        {
            return Error(500, "Database query failed.");
        }
        catch
        {
            return Error(500, "Internal server error.");
        }
    }

    public static async Task<IResult> Login(LoginRequest request, SupabaseUsers users, SessionTokens tokens, CancellationToken cancellationToken)
    {
        if (!users.IsConfigured) return Error(503, "Database service is not configured.");
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrEmpty(request.Password))
            return Error(400, "Missing usernameOrEmail or password.");
        if (request.UsernameOrEmail.Length > 256 || request.Password.Length > 256)
            return Error(400, "Invalid request fields.");
        try
        {
            var user = await users.FindByLoginAsync(request.UsernameOrEmail, cancellationToken);
            if (user is null) return Error(400, "Account not found.");
            var legacy = !user.PasswordHash.StartsWith("pbkdf2-sha256:600000:", StringComparison.Ordinal);
            var actual = Pbkdf2(request.Password, Convert.FromHexString(user.Salt), legacy ? 10_000 : 600_000);
            var expectedText = legacy ? user.PasswordHash : user.PasswordHash["pbkdf2-sha256:600000:".Length..];
            byte[] expected;
            try { expected = Convert.FromHexString(expectedText); }
            catch (FormatException) { return Error(400, "Invalid password."); }
            if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
                return Error(400, "Invalid password.");
            if (!user.IsVerified) return Error(400, "Please verify your email before logging in.");
            return Results.Ok(new
            {
                message = "Login successful.",
                token = tokens.Issue(user),
                user = new { username = user.Username, email = user.Email }
            });
        }
        catch (SupabaseException)
        {
            return Error(500, "Database query failed.");
        }
        catch
        {
            return Error(500, "Internal server error.");
        }
    }

    private static byte[] Pbkdf2(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);
}
