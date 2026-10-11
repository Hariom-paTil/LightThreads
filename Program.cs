using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// Bind strictly to local port 5080
builder.WebHost.UseUrls("http://127.0.0.1:5080");

builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

// 1. Root route: Serve index.html for host session creation
app.MapGet("/", () =>
{
    var htmlPath = Path.Combine(app.Environment.ContentRootPath, "index.html");
    if (!File.Exists(htmlPath)) return Results.NotFound();
    return Results.Content(File.ReadAllText(htmlPath), "text/html");
});

// 2. PIN Route: Validate PIN. If revoked or invalid, return standard HTTP 404 NotFound
app.MapGet("/{pin}", (string pin, SessionStore store) =>
{
    var session = store.GetByPin(pin);
    if (session == null)
    {
        return Results.NotFound();
    }

    var htmlPath = Path.Combine(app.Environment.ContentRootPath, "index.html");
    if (!File.Exists(htmlPath)) return Results.NotFound();
    return Results.Content(File.ReadAllText(htmlPath), "text/html");
});

// API endpoint to create a new session
app.MapPost("/api/session/create", (SessionStore store) =>
{
    var session = store.CreateSession();
    return Results.Ok(new { sessionId = session.Id, pin = session.CurrentPin });
});

app.MapHub<ChatHub>("/chat");

app.Run();

// =========================================================
// TYPES & SESSION MANAGEMENT
// =========================================================

public class ChatSession
{
    public string Id { get; }
    public string CurrentPin { get; set; }
    public string HostConnectionId { get; set; } = string.Empty;

    public ChatSession(string id, string initialPin)
    {
        Id = id;
        CurrentPin = initialPin;
    }
}

public class SessionStore
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessionsById = new();
    private readonly ConcurrentDictionary<string, ChatSession> _sessionsByPin = new();

    public ChatSession CreateSession()
    {
        string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLower();
        string pin = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        var session = new ChatSession(sessionId, pin);
        _sessionsById[sessionId] = session;
        _sessionsByPin[pin] = session;
        return session;
    }

    public ChatSession? GetByPin(string pin)
    {
        _sessionsByPin.TryGetValue(pin, out var session);
        return session;
    }

    public ChatSession? GetById(string sessionId)
    {
        _sessionsById.TryGetValue(sessionId, out var session);
        return session;
    }

    public string RotatePin(ChatSession session)
    {
        _sessionsByPin.TryRemove(session.CurrentPin, out _);

        string newPin = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        session.CurrentPin = newPin;
        _sessionsByPin[newPin] = session;

        return newPin;
    }
}

// =========================================================
// SIGNALR HUB WITH USERNAME TRACKING & URL ROTATION
// =========================================================

public class ChatHub : Hub
{
    private readonly SessionStore _sessionStore;
    private static readonly ConcurrentDictionary<string, (ChatSession Session, string GuestName)> PendingKnocks = new();

    public ChatHub(SessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    public async Task RegisterHostSession(string sessionId)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            session.HostConnectionId = Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, session.Id);
        }
    }

    public async Task KnockWithPin(string pin, string username)
    {
        var session = _sessionStore.GetByPin(pin);
        if (session == null || string.IsNullOrEmpty(session.HostConnectionId))
        {
            await Clients.Caller.SendAsync("SystemError", "Invalid or expired URL.");
            return;
        }

        PendingKnocks[Context.ConnectionId] = (session, username);
        await Clients.Client(session.HostConnectionId).SendAsync("PendingApprovalRequest", Context.ConnectionId, username, pin);
    }

    public async Task RespondToKnock(string guestConnectionId, bool approved)
    {
        if (!PendingKnocks.TryRemove(guestConnectionId, out var knockData))
        {
            return;
        }

        var session = knockData.Session;
        var guestName = knockData.GuestName;

        if (approved)
        {
            await Groups.AddToGroupAsync(guestConnectionId, session.Id);
            await Clients.Client(guestConnectionId).SendAsync("JoinApproved", session.Id, session.CurrentPin);
            await Clients.Group(session.Id).SendAsync("ReceiveMessage", "SYSTEM", $"{guestName} joined the session.");
        }
        else
        {
            await Clients.Client(guestConnectionId).SendAsync("JoinDenied");

            string newPin = _sessionStore.RotatePin(session);
            await Clients.Group(session.Id).SendAsync("ShiftUrlToNewPin", newPin);
        }
    }

    public async Task SendGroupMessage(string sessionId, string user, string message)
    {
        await Clients.Group(sessionId).SendAsync("ReceiveMessage", user, message);
    }
}