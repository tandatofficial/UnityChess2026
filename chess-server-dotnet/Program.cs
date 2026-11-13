using System.Security.Claims;
using System.Text.Json;
using System.Diagnostics;
using ChessServer;
using ChessServer.Auth;
using ChessServer.Game;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "11100"}");
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddHttpClient();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true)));
builder.Services.AddSingleton(sp => new SessionTokens(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));
builder.Services.AddSingleton<SupabaseUsers>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<MatchService>();
builder.Services.AddHostedService<MatchTicker>();
builder.Services.AddAuthentication("Session")
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("Session", _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();
_ = app.Services.GetRequiredService<SessionTokens>();
var serverStarted = Stopwatch.GetTimestamp();
app.UseCors();
app.UseMiddleware<AuthRateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", (MatchService matches) => Results.Ok(new { status = "online", activeRooms = matches.RoomCount }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", uptime = (long)Stopwatch.GetElapsedTime(serverStarted).TotalSeconds, timestamp = DateTimeOffset.UtcNow }));
app.MapPost("/api/auth/register", async (RegisterRequest request, SupabaseUsers users, EmailSender email, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
    await AuthEndpoints.Register(request, users, email, loggerFactory, cancellationToken));
app.MapPost("/api/auth/verify", async (VerifyRequest request, SupabaseUsers users, CancellationToken cancellationToken) =>
    await AuthEndpoints.Verify(request, users, cancellationToken));
app.MapPost("/api/auth/login", async (LoginRequest request, SupabaseUsers users, SessionTokens tokens, CancellationToken cancellationToken) =>
    await AuthEndpoints.Login(request, users, tokens, cancellationToken));

var rooms = app.MapGroup("/api/rooms").RequireAuthorization();
rooms.MapGet("/", (MatchService matches) => Results.Ok(matches.ListRooms()));
rooms.MapPost("/", async (CreateRoomRequest request, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.CreateRoomAsync(user, request, hub));
rooms.MapPost("/{roomId}/join", async (string roomId, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.JoinRoomAsync(user, roomId, hub));
rooms.MapGet("/{roomId}", async (string roomId, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.GetStateAsync(user, roomId, hub));
rooms.MapPost("/{roomId}/moves", async (string roomId, MoveRequest move, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.MoveAsync(user, roomId, move, hub));
rooms.MapPost("/{roomId}/resign", async (string roomId, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.ResignAsync(user, roomId, hub));
rooms.MapPost("/{roomId}/leave", async (string roomId, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.LeaveAsync(user, roomId, hub));
rooms.MapPost("/{roomId}/rematch", async (string roomId, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.RequestRematchAsync(user, roomId, hub));
rooms.MapPost("/{roomId}/ready", async (string roomId, ClaimsPrincipal user, MatchService matches, IHubContext<GameHub> hub) =>
    await matches.ReadyForRematchAsync(user, roomId, hub));

app.MapHub<GameHub>("/hubs/game").RequireAuthorization();
app.Run();

public partial class Program;
