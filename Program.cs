
var builder = WebApplication.CreateBuilder(args);

// Keep the development web server accessible only
// from this computer for now.
builder.WebHost.UseUrls("http://127.0.0.1:5080");

var app = builder.Build();

// Basic security headers for the embedded page.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'none'; style-src 'unsafe-inline'; " +
        "script-src 'unsafe-inline'; connect-src 'self'; " +
        "img-src 'none'; object-src 'none'; base-uri 'none'; " +
        "frame-ancestors 'none'; form-action 'none'";

    await next();
});

// The HTML, CSS and JavaScript are embedded in C#.
app.MapGet("/", () =>
{
    var html = """
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>LightThread</title>

        <style>
            * { box-sizing: border-box; }

            body {
                margin: 0;
                font-family: Arial, sans-serif;
                background: #f1f5f9;
                color: #172033;
                min-height: 100vh;
                display: grid;
                place-items: center;
                padding: 20px;
            }

            .chat {
                width: 100%;
                max-width: 520px;
                height: min(680px, 90vh);
                background: white;
                border-radius: 16px;
                box-shadow: 0 8px 30px #00000012;
                display: flex;
                flex-direction: column;
                overflow: hidden;
            }

            header {
                padding: 20px;
                background: #173b67;
                color: white;
            }

            header h1 {
                margin: 0 0 6px;
                font-size: 24px;
            }

            header p {
                margin: 0;
                color: #dbeafe;
                font-size: 13px;
            }

            #messages {
                flex: 1;
                padding: 20px;
                overflow-y: auto;
            }

            .notice {
                background: #f1f5f9;
                border-radius: 10px;
                padding: 12px;
                color: #475569;
                font-size: 14px;
                line-height: 1.5;
            }

            form {
                display: flex;
                gap: 8px;
                padding: 14px;
                border-top: 1px solid #e2e8f0;
            }

            input {
                min-width: 0;
                flex: 1;
                padding: 12px;
                border: 1px solid #cbd5e1;
                border-radius: 8px;
                font-size: 15px;
            }

            button {
                border: 0;
                border-radius: 8px;
                padding: 12px 16px;
                background: #2563eb;
                color: white;
                font-weight: bold;
                cursor: pointer;
            }

            button:disabled, input:disabled {
                opacity: .55;
                cursor: not-allowed;
            }
        </style>
    </head>

    <body>
        <main class="chat">
            <header>
                <h1>PeerChat</h1>
                <p id="status">Private chat · Not connected</p>
            </header>

            <section id="messages" aria-live="polite">
                <div class="notice">
                    Welcome to LightThread!<br><br>
                    Your local chat interface is ready.
                    Messaging is not connected yet.
                    We will add secure invitations and
                    real network communication next.
                </div>
            </section>

            <form id="chatForm">
                <input
                    id="message"
                    type="text"
                    maxlength="2000"
                    placeholder="Messaging is not ready yet..."
                    aria-label="Message"
                    disabled
                >
                <button type="submit" disabled>Send</button>
            </form>
        </main>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

