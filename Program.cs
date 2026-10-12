using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Bind strictly to local port 5080
builder.WebHost.UseUrls("http://127.0.0.1:5080");

// 1. Hardened SignalR payload limit (10 KB max)
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024;
});

builder.Services.AddSingleton<SessionStore>();

// 2. IP-based Rate Limiting reading real client IP behind Cloudflare Tunnel Proxy
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        string clientIp = httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

app.UseRateLimiter();

// 3. Strict Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; style-src 'self' 'unsafe-inline';");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    await next();
});

// Serve static encryption script
app.MapGet("/encryption.js", () =>
{
    var jsPath = Path.Combine(app.Environment.ContentRootPath, "encryption.js");
    if (!File.Exists(jsPath)) return Results.NotFound();
    return Results.Content(File.ReadAllText(jsPath), "application/javascript");
});

// Serve index.html for root route
app.MapGet("/", () =>
{
    var htmlPath = Path.Combine(app.Environment.ContentRootPath, "index.html");
    if (!File.Exists(htmlPath)) return Results.NotFound();
    return Results.Content(File.ReadAllText(htmlPath), "text/html");
});

// PIN Route: Validate PIN. If revoked or invalid, return standard HTTP 404
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

app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(() => StartCloudflareTunnel(autoOpenBrowser: true));
});

app.Run();

static void OpenBrowser(string url)
{
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[BROWSER] Could not auto-open browser: {ex.Message}");
    }
}

static void StartCloudflareTunnel(bool autoOpenBrowser)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n[LIGHTTHREADS] Starting Cloudflare Tunnel process...");
    Console.ResetColor();

    string executablePath = "cloudflared";

    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    string[] candidatePaths = new[]
    {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe"),
        Path.Combine(Directory.GetCurrentDirectory(), "cloudflared.exe"),
        Path.Combine(programFiles, "cloudflared", "cloudflared.exe"),
        Path.Combine(userProfile, @"AppData\Local\Microsoft\WinGet\Links\cloudflared.exe")
    };

    foreach (var path in candidatePaths)
    {
        if (File.Exists(path))
        {
            executablePath = path;
            break;
        }
    }

    var processInfo = new ProcessStartInfo
    {
        FileName = executablePath,
        Arguments = "tunnel --url http://127.0.0.1:5080",
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    try
    {
        var process = new Process { StartInfo = processInfo };
        bool browserLaunched = false;

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                var match = Regex.Match(e.Data, @"https://[-a-zA-Z0-9@:%._\+~#=]{1,256}\.trycloudflare\.com");
                if (match.Success && !browserLaunched)
                {
                    browserLaunched = true;
                    string publicUrl = match.Value;

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n=========================================================");
                    Console.WriteLine($" [PUBLIC HTTPS URL ACTIVE]");
                    Console.WriteLine($" {publicUrl}");
                    Console.WriteLine(" Tunnel ready! Launching browser in 3 seconds...");
                    Console.WriteLine("=========================================================\n");
                    Console.ResetColor();

                    if (autoOpenBrowser)
                    {
                        Task.Delay(3000).ContinueWith(_ => OpenBrowser(publicUrl));
                    }
                }
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[ERROR] Could not launch cloudflared: {ex.Message}");
        Console.ResetColor();

        if (autoOpenBrowser)
        {
            OpenBrowser("http://127.0.0.1:5080");
        }
    }
}

// =========================================================
// TYPES & SESSION MANAGEMENT WITH CLEANUP SUPPORT
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

    public void RemoveSession(string sessionId)
    {
        if (_sessionsById.TryRemove(sessionId, out var session))
        {
            _sessionsByPin.TryRemove(session.CurrentPin, out _);
        }
    }
}

// =========================================================
// SIGNALR HUB WITH DISCONNECTION & CLEANUP HANDLERS
// =========================================================

public class ChatHub : Hub
{
    private readonly SessionStore _sessionStore;
    private static readonly ConcurrentDictionary<string, (ChatSession Session, string GuestName)> PendingKnocks = new();
    private static readonly ConcurrentDictionary<string, string> ConnectionToSessionMap = new();

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
            ConnectionToSessionMap[Context.ConnectionId] = session.Id;
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
            ConnectionToSessionMap[guestConnectionId] = session.Id;
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
        if (message.Length > 2048) return;
        await Clients.Group(sessionId).SendAsync("ReceiveMessage", user, message);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        PendingKnocks.TryRemove(Context.ConnectionId, out _);

        if (ConnectionToSessionMap.TryRemove(Context.ConnectionId, out string? sessionId))
        {
            var session = _sessionStore.GetById(sessionId);
            if (session != null)
            {
                if (session.HostConnectionId == Context.ConnectionId)
                {
                    await Clients.Group(sessionId).SendAsync("ReceiveMessage", "SYSTEM", "Host has left. Room closed.");
                    _sessionStore.RemoveSession(sessionId);
                }
                else
                {
                    await Clients.Group(sessionId).SendAsync("ReceiveMessage", "SYSTEM", "A participant has disconnected.");
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}