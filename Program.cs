using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

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

// 2. PIN Route: Validate PIN. If revoked or invalid, return standard HTTP 404
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

// ---------------------------------------------------------
// AUTOMATIC CLOUDFLARE & BROWSER LAUNCHER
// ---------------------------------------------------------
app.Lifetime.ApplicationStarted.Register(() =>
{
    // 1. Open the local web page in the host's default browser
    OpenBrowser("http://127.0.0.1:5080");

    // 2. Start Cloudflare Tunnel in background
    Task.Run(() => StartCloudflareTunnel());
});

app.Run();

// Cross-platform helper method to launch default web browser
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

// Helper method to execute cloudflared and capture the public URL
static void StartCloudflareTunnel()
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n[LIGHTTHREADS] Starting Cloudflare Tunnel process...");
    Console.ResetColor();

    string executablePath = "cloudflared"; // Default system PATH lookup

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

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                var match = Regex.Match(e.Data, @"https://[-a-zA-Z0-9@:%._\+~#=]{1,256}\.trycloudflare\.com");
                if (match.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n=========================================================");
                    Console.WriteLine($" [PUBLIC HTTPS URL ACTIVE]");
                    Console.WriteLine($" {match.Value}");
                    Console.WriteLine(" Share this public address with your friend anywhere in the world!");
                    Console.WriteLine("=========================================================\n");
                    Console.ResetColor();
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
    }
}

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