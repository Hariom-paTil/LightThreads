# LightThreads

LightThreads is an open-source, peer-to-host communication application built with .NET 9, ASP.NET Core Minimal APIs, and SignalR. It allows users to host temporary chat sessions on their own computers and invite others to join through a shareable URL.

Guests can open the invitation link in a browser without installing any additional software. Cloudflare Tunnel provides public HTTPS access to the locally running application without requiring manual port forwarding.

## Project Status

LightThreads is currently under active development.

The current implementation includes real-time messaging, host-controlled guest approval, dynamic room routing, and Cloudflare Tunnel integration. Rate limiting, end-to-end encryption, and file sharing are planned for future releases.

## Features

- **Temporary messaging:** Messages are stored in application memory rather than a database. Chat history is not designed to persist after the relevant in-memory state is cleared.
- **Self-hosted application:** The host runs the application on their own computer without maintaining a dedicated application server.
- **Remote access:** Guests can connect from different networks using a public HTTPS URL.
- **Waiting room:** New guests must be approved by the host before joining a chat session.
- **Dynamic invitation routes:** The application can replace an invitation route after a guest request is rejected.
- **Route protection:** Invalid or revoked room routes return HTTP 404 responses.
- **Cloudflare Tunnel integration:** The application can launch the Cloudflare Tunnel client to expose the local server.
- **Browser-based interface:** The client uses HTML, CSS, and JavaScript and does not require a separate client installation.
- **Real-time communication:** SignalR handles messaging and live session events.

## Architecture

LightThreads follows a peer-to-host architecture. The host runs the application locally, while remote guests connect through Cloudflare's network.

```text
Host Computer
    |
    |-- Host Browser
    |
    |-- ASP.NET Core Application
    |       |
    |       |-- Minimal API endpoints
    |       |-- SignalR Hub
    |       |-- Session Store
    |
    |-- Cloudflare Tunnel Client
                |
                v
         Cloudflare Network
                |
                | HTTPS / WebSocket
                v
           Guest Browser
```

The application server runs on the host's computer. Cloudflare Tunnel provides the connection between the public URL and the local application.

### Communication Flow

1. The host starts LightThreads.
2. ASP.NET Core starts the local web server.
3. The application launches Cloudflare Tunnel.
4. A public HTTPS URL is displayed in the terminal.
5. The host opens the application and creates a chat session.
6. The host shares the invitation URL with a guest.
7. The guest opens the link and requests access.
8. The host approves or rejects the request.
9. Approved guests can exchange messages through SignalR.

This is not a direct peer-to-peer connection. Messages are handled by the application running on the host's computer, and remote traffic passes through Cloudflare infrastructure.

## Waiting Room and Invitation Management

LightThreads includes a waiting room that allows the host to control who can enter a session.

The intended workflow is:

1. A host creates a session and receives an invitation URL.
2. A guest opens the URL and requests access.
3. The application notifies the host about the pending request.
4. The host decides whether to accept or reject the guest.
5. Accepted guests can join the authorized session.
6. When a request is rejected, the application can revoke the previous invitation route and generate a replacement.

The implementation is intended to preserve existing conversations when an invitation route changes. Actual behavior depends on the current session and routing implementation.

Returning HTTP 404 for an invalid route helps conceal unavailable sessions, but it does not replace server-side authorization.

## Security

Security is an important part of the project, but LightThreads is still under development and has not been presented as an independently audited messaging platform.

### Current Security Measures

| Mechanism | Purpose |
| --- | --- |
| Cryptographically secure random number generation | Generate unpredictable invitation codes |
| Safe DOM rendering and input escaping | Reduce the risk of cross-site scripting |
| HTTPS and WSS | Protect data in transit |
| Route revocation | Reject requests to revoked invitation routes |
| In-memory session storage | Avoid storing chat history in a database |

### Current Limitations

**End-to-end encryption is not implemented yet.**

HTTPS and WebSocket Secure protect communication in transit, but they do not provide end-to-end encryption. The application and infrastructure handling the traffic may still be able to access message contents.

**Rate limiting is planned.**

Short invitation codes can be vulnerable to repeated guessing attempts. Rate limiting and additional abuse protection should be implemented before exposing the application to unrestricted public use.

**Route secrecy is not authorization.**

An unpredictable invitation URL can make casual discovery more difficult, but every protected operation must still verify that the requesting connection is authorized.

**Temporary storage does not guarantee complete data removal.**

Messages are not intended to be stored persistently by the application. However, operating-system artifacts, diagnostic logs, browser data, or network metadata may remain depending on the environment and configuration.

**Cloudflare is part of the trust boundary.**

The current architecture relies on Cloudflare for public connectivity. It does not guarantee that message contents are hidden from every intermediary.

These limitations will be considered as the project develops.

## Technology Stack

| Technology | Purpose |
| --- | --- |
| C# | Application development |
| .NET 9 | Runtime and framework |
| ASP.NET Core Minimal APIs | HTTP endpoints and routing |
| SignalR | Real-time messaging |
| HTML5 | Web interface |
| CSS | Interface styling |
| JavaScript | Client-side behavior and SignalR communication |
| Cloudflare Tunnel | Public access to the local application |
| Web Crypto API | Planned client-side encryption |

## Prerequisites

Before running the project, install the following:

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/)
- A modern web browser
- An active internet connection

The `cloudflared` executable must be available in the project directory or through the system PATH, depending on the current launcher implementation.

The project currently targets .NET 9. Check the official .NET support policy when choosing a runtime for long-term use.

## Getting Started

### 1. Clone the Repository

Replace `your-username` with the GitHub username or organization that owns the repository.

```bash
git clone https://github.com/your-username/LightThreads.git
cd LightThreads
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Run the Application

```bash
dotnet run
```

### 4. Open the Application

When configured correctly, the local application should be available at:

```text
http://127.0.0.1:5080
```

The application also launches Cloudflare Tunnel and displays a public URL in the terminal.

Example terminal output:

```text
=========================================================
                    LIGHTTHREADS
=========================================================

Local Application:
http://127.0.0.1:5080

Public HTTPS URL:
https://example.trycloudflare.com

Open the application and create a chat session.
Share the invitation URL with your guest.

=========================================================
```

The public URL shown above is only an example. Your actual URL will depend on the tunnel configuration.

### 5. Create a Chat Session

1. Open the local application in your browser.
2. Enter your display name.
3. Select Create Session.
4. Copy the generated invitation URL.
5. Share the link with your guest.
6. Approve the guest through the waiting room.
7. Start chatting.

The guest only needs a compatible browser and an internet connection.

## Project Structure

```text
LightThreads/
|
|-- Program.cs
|   |-- ASP.NET Core Minimal APIs
|   |-- SignalR ChatHub
|   |-- SessionStore
|   |-- Cloudflare Tunnel Launcher
|
|-- index.html
|   |-- HTML interface
|   |-- CSS styling
|   |-- JavaScript client
|
|-- cloudflared.exe
|   |-- Cloudflare Tunnel executable for Windows
|
|-- LightThreads.csproj
|   |-- Project configuration
|
|-- README.md
|
|-- LICENSE
```

This structure describes the current intended layout and may change as development continues.

The `cloudflared.exe` executable is specific to Windows. Other operating systems require a compatible Cloudflare Tunnel installation.

## Roadmap

### Completed or Implemented

- Real-time messaging using SignalR
- Host-controlled waiting room
- Guest approval and rejection
- Dynamic invitation-route rotation
- Cloudflare Tunnel integration
- Browser-based chat interface

### Planned

**Rate Limiting and Abuse Protection**

Add request limits and protections against invitation-code guessing and connection abuse.

**End-to-End Encryption**

Explore client-side encryption using the Web Crypto API, including AES-GCM and a secure key exchange mechanism.

**File Sharing**

Allow authorized participants to exchange files temporarily.

**Security Improvements**

Improve session authorization, connection lifecycle handling, validation, and security testing.

**Cross-Platform Support**

Improve installation, tunnel discovery, and startup behavior across Windows, macOS, and Linux.

## Contributing

Contributions, bug reports, and suggestions are welcome.

1. Fork the repository.
2. Create a branch for your changes.
3. Implement and test your changes.
4. Submit a pull request explaining what you changed.

For security vulnerabilities, avoid publishing exploitable details before maintainers have had an opportunity to investigate.

## License

LightThreads is distributed under the MIT License. Refer to the `LICENSE` file for the complete license terms.

## About the Project

LightThreads explores an approach to temporary communication in which users can host a chat application on their own computers and invite others through a browser-accessible URL.

The project focuses on self-hosting, temporary sessions, host-controlled access, and real-time messaging without requiring guests to install a client application.

The goal is to make it straightforward to start a conversation, share access with selected participants, and end the session when it is no longer needed.

The project is open source and continues to evolve as new features and security improvements are developed.