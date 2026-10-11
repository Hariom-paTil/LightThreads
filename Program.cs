using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// 1. Bind strictly to local port 5080
builder.WebHost.UseUrls("http://127.0.0.1:5080");

// 2. Add SignalR service
builder.Services.AddSignalR();

// 3. Register Session Store as a Singleton service
builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

// 4. Serve index.html from disk
app.MapGet("/", () =>
{
    var htmlPath = Path.Combine(app.Environment.ContentRootPath, "index.html");
    if (!File.Exists(htmlPath))
    {
        return Results.NotFound("index.html file not found in project directory.");
    }
    return Results.Content(File.ReadAllText(htmlPath), "text/html");
});

// 5. API endpoint for Host to generate a private invitation code
app.MapPost("/api/session/create", (SessionStore store) =>
{
    var session = store.CreateSession();
    return Results.Ok(new { sessionId = session.Id, token = session.Token });
});

// 6. Map SignalR Hub
app.MapHub<ChatHub>("/chat");

// 7. Execute Application
app.Run();

// =========================================================
// TYPES & CLASS DECLARATIONS
// =========================================================

public record ChatSession(string Id, string Token, DateTime CreatedAtUtc, bool IsRevoked = false);

public class SessionStore
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();

    public ChatSession CreateSession()
    {
        string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLower();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLower();

        var session = new ChatSession(sessionId, token, DateTime.UtcNow);
        _sessions[sessionId] = session;
        return session;
    }

    public bool ValidateToken(string sessionId, string token)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return !session.IsRevoked && session.Token == token;
        }
        return false;
    }
}

public class ChatHub : Hub
{
    private readonly SessionStore _sessionStore;

    public ChatHub(SessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    public async Task JoinSession(string sessionId, string token, string username)
    {
        if (!_sessionStore.ValidateToken(sessionId, token))
        {
            await Clients.Caller.SendAsync("SystemError", "Access Denied: Invalid or revoked session token.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        await Clients.Group(sessionId).SendAsync("ReceiveMessage", "SYSTEM", $"{username} joined the session.");
    }

    public async Task SendGroupMessage(string sessionId, string token, string user, string message)
    {
        if (!_sessionStore.ValidateToken(sessionId, token))
        {
            await Clients.Caller.SendAsync("SystemError", "Unauthorized message attempt.");
            return;
        }

        await Clients.Group(sessionId).SendAsync("ReceiveMessage", user, message);
    }
}