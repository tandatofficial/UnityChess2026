using System.Collections.Concurrent;
using System.Text.Json;

namespace ChessServer.Auth;

public sealed class AuthRateLimitMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> Attempts = new();

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await next(context);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var entry = Attempts.AddOrUpdate(key,
            _ => (1, now.AddMinutes(1)),
            (_, old) => old.Until <= now ? (1, now.AddMinutes(1)) : (old.Count + 1, old.Until));
        if (Attempts.Count > 10_000)
            foreach (var old in Attempts.Where(x => x.Value.Until <= now).Select(x => x.Key)) Attempts.TryRemove(old, out _);

        if (entry.Count > 20)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Too many requests. Try again in a minute." });
            return;
        }

        if (context.Request.ContentLength is > 16 * 1024 ||
            (context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true &&
             context.Request.Method == HttpMethods.Post &&
             (context.Request.ContentLength ?? 0) > 16 * 1024))
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = "Request body is too large." });
            return;
        }
        await next(context);
    }
}
