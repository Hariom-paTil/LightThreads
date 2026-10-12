# LightThreads

![.NET](https://img.shields.io/badge/.NET-8%2F9-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows&logoColor=white)
![Security](https://img.shields.io/badge/Encryption-AES--256--GCM-2E8B57)
![License](https://img.shields.io/badge/License-MIT-blue.svg)

**Ultra-lightweight, zero-trace, end-to-end encrypted private chat engine built with ASP.NET Core SignalR and the Web Crypto API.**

LightThreads is a disposable 1-on-1 messaging application that lets a host establish a private chat session and invite a remote guest through a shareable HTTPS URL. It combines browser-side encryption, an in-memory SignalR relay, automated Cloudflare tunneling, and host-controlled access without requiring guests to install a client application.

The goal is simple: communicate privately, avoid persistent server-side chat history, and keep session state temporary.

## Overview

LightThreads turns the host's Windows machine into a temporary communication endpoint. The host launches the application, establishes a public HTTPS tunnel, and shares the generated invitation URL with a guest. The guest opens the URL in a browser, requests access, and waits for host approval.

Messages are encrypted in the browser before being transmitted. The server relays encrypted payloads between connected clients without needing the plaintext message or encryption key.

### Design Principles

- **Client-side encryption:** Message encryption and decryption happen in the browser.
- **Zero message persistence:** Chat messages are not intentionally stored in a database or written to application log files.
- **Temporary sessions:** Active rooms and session state exist in memory and disappear when the application stops or clears the session.
- **Host-controlled access:** Guests require host approval before joining a session.
- **Simple deployment:** Run the published Windows application or build from source.
- **Public HTTPS access:** Cloudflare Tunnel exposes the local application without manual port forwarding.

## Features

| Feature | Implementation | Purpose |
|---|---|---|
| End-to-End Encryption | AES-256-GCM with Web Crypto API | Encrypt messages in the browser |
| Key Derivation | PBKDF2 with room-bound salt | Derive encryption keys locally |
| Real-Time Messaging | ASP.NET Core SignalR | Relay messages between connected browsers |
| In-Memory Sessions | `ConcurrentDictionary` | Manage temporary room and session state |
| Public HTTPS Access | Cloudflare Tunnel | Expose the local application |
| Host Waiting Room | Knock and approval workflow | Require host authorization |
| Dynamic Session Rotation | New URL and PIN after rejection | Invalidate the previous invitation route |
| Cloaked Routes | HTTP 404 for invalid or revoked PIN routes | Avoid revealing route validity through distinct error responses |
| Rate Limiting | 15 requests per minute per IP | Reduce request abuse |
| SignalR Payload Limits | 10 KB maximum payload | Restrict oversized messages |
| Browser Log Cleanup | Maximum 50 displayed entries | Limit retained chat history in the interface |
| Content Security Policy | Strict CSP headers | Restrict unsafe browser resource execution |
| Client IP Extraction | Cloudflare `CF-Connecting-IP` | Identify the original client IP behind a trusted proxy |

## Architecture and Data Flow

LightThreads uses a relay-based architecture. Clients exchange encrypted message payloads through SignalR, while encryption keys are intended to remain on the client side.

```text
+---------------------------+
|         Client A          |
|      Browser / Web UI     |
+-------------+-------------+
              |
              v
+---------------------------+
| Web Crypto API            |
| AES-256-GCM Encryption    |
| PBKDF2 Key Derivation     |
+-------------+-------------+
              |
              v
+---------------------------+
| Encrypted Payload         |
| Base64-Encoded Ciphertext |
+-------------+-------------+
              |
              v
+---------------------------+
|     Cloudflare Tunnel     |
|       Public HTTPS        |
+-------------+-------------+
              |
              v
+---------------------------+
|   ASP.NET Core Server     |
|       SignalR Relay       |
|  In-Memory Session State  |
+-------------+-------------+
              |
              v
+---------------------------+
| Encrypted Payload         |
| Base64-Encoded Ciphertext |
+-------------+-------------+
              |
              v
+---------------------------+
|         Client B          |
| Web Crypto API Decryption |
+-------------+-------------+
              |
              v
+---------------------------+
|      Plaintext Message    |
|     Rendered in Browser   |
+---------------------------+
```

### Message Flow

1. The sender composes a message in the browser.
2. The Web Crypto API encrypts the message using an AES-256-GCM key derived locally.
3. The ciphertext is encoded as Base64 for transport.
4. Cloudflare Tunnel forwards the HTTPS connection to the local ASP.NET Core application.
5. SignalR relays the encrypted payload to the authorized recipient.
6. The recipient decrypts the payload in their browser and renders the message.
7. The server handles routing and connection state without needing access to plaintext messages.

Base64 is an encoding format, not encryption. Confidentiality comes from AES-GCM.

## Session Lifecycle and Knock Flow

Every session is controlled by the host. A guest must request access before joining the chat.

```text
+---------------------------+
| Host launches application |
+-------------+-------------+
              |
              v
+---------------------------+
| Create session and PIN    |
| Start public HTTPS tunnel |
+-------------+-------------+
              |
              v
+---------------------------+
| Share invitation URL     |
| and required secret      |
+-------------+-------------+
              |
              v
+---------------------------+
| Guest opens invitation    |
| and requests access       |
+-------------+-------------+
              |
              v
+---------------------------+
| Guest enters waiting room |
|       (Knock request)     |
+-------------+-------------+
              |
              v
+---------------------------+
| Host reviews request      |
+-------------+-------------+
              |
        +-----+-----+
        |           |
        v           v
+---------------+  +--------------------+
|    APPROVE    |  |       DENY         |
+-------+-------+  +---------+----------+
        |                    |
        v                    v
+---------------+  +--------------------+
| Authorize     |  | Revoke active PIN  |
| guest session |  | route and rotate   |
+-------+-------+  | URL and secret     |
        |          +---------+----------+
        v                    |
+---------------+            v
| Encrypted     |  +--------------------+
| chat begins   |  | Old route returns  |
+---------------+  | HTTP 404           |
                   +--------------------+
```

The rejection workflow is designed to invalidate the previous invitation route and rotate the active URL and secret. A new invitation must be shared with any participant who needs access to the replacement session.

## Quick Start / Downloads

### Option A: Download for Windows

**Recommended for users who want to run LightThreads without building from source.**

1. Open the project's GitHub repository.
2. Download `LightThreadsApplication.zip` from the repository's available download or release section.
3. Extract the ZIP file to a folder on your Windows PC.
4. Open the extracted `LightThreadsApplication` folder.
5. Run `LightThreads.exe`.
6. Follow the application instructions to create a session and obtain the public HTTPS invitation URL.
7. Share the invitation details with your guest.
8. Approve the guest's connection request when it appears.

No .NET SDK is required when using the self-contained published application. Ensure any required dependencies, including `cloudflared` if it is not bundled, are available.

### Option B: Build from Source

**Requirements:**

- Git
- A compatible .NET SDK matching the project's target framework
- Windows for the documented deployment workflow
- Cloudflare Tunnel (`cloudflared`) available to the application, unless bundled with the project

#### 1. Clone the repository

```bash
git clone <YOUR_REPOSITORY_URL>
cd LightThreads
```

Replace `<YOUR_REPOSITORY_URL>` with your actual repository URL.

#### 2. Restore dependencies

```bash
dotnet restore
```

#### 3. Run the application

```bash
dotnet run
```

The default application port is `5080`, unless overridden by the project configuration.

Follow the application output and instructions to obtain the local endpoint and public HTTPS URL.

## Security and Threat Model

LightThreads is designed to minimize server-side exposure of chat content. Its security depends on correct browser-side cryptography, safe secret distribution, access-control enforcement, and deployment configuration.

### Security Specifications

| Area | Specification | Security Objective |
|---|---|---|
| Message Encryption | AES-256-GCM | Confidentiality and authenticated encryption |
| Key Derivation | PBKDF2 with room-bound salt | Derive keys locally from the shared secret |
| Transport | HTTPS through Cloudflare Tunnel | Protect network traffic in transit |
| Message Storage | In-memory relay; no intentional persistent message storage | Avoid retaining chat history on the server |
| Session State | In-memory `ConcurrentDictionary` | Keep active room state temporary |
| Route Invalidation | PIN revocation and rotation | Invalidate old invitation routes |
| Invalid PIN Handling | HTTP 404 | Avoid distinguishing invalid routes through separate status responses |
| Request Throttling | 15 requests per minute per IP | Reduce excessive requests |
| SignalR Payload Limit | 10 KB | Restrict oversized payloads |
| Browser UI History | Maximum 50 displayed entries | Limit client-side displayed history |
| Browser Resource Policy | Strict Content Security Policy | Reduce exposure to script injection and unauthorized resource loading |
| Client IP Handling | `CF-Connecting-IP` behind Cloudflare | Identify the originating IP when the proxy is trusted |

### Threat Model

| Threat | Mitigation | Limitations |
|---|---|---|
| Network interception | HTTPS and client-side encryption | Does not protect compromised endpoints |
| Server reading messages | Client-side AES-GCM encryption | Depends on correct key handling and trustworthy client code |
| Unauthorized guest access | Host approval and session routing | Must be enforced server-side, not only in the UI |
| Reuse of a rejected invitation | PIN revocation and route rotation | Existing connections must also be invalidated |
| Excessive requests | IP-based rate limiting | Distributed abuse may require additional controls |
| Oversized SignalR messages | Payload limits | Does not replace application-level validation |
| Persistent message leakage | No intentional message logging or database storage | Proxy, crash, operating system, or hosting logs may still retain metadata |
| Malicious browser content | CSP and safe DOM rendering | CSP and rendering practices require review |
| Compromised host or guest | No complete application-level mitigation | An endpoint can capture plaintext, keys, or screenshots |
| Weak shared secret | PBKDF2 key derivation | A six-digit PIN remains susceptible to guessing |

### Cryptographic Details

LightThreads uses a room-bound salt with the format:

```text
LightThreadsSalt_{PIN}_2026
```

PBKDF2 derives an encryption key locally, and AES-256-GCM encrypts message content before transmission.

For a production security review, verify the following:

- Use a cryptographically secure random number generator for session secrets, salts where appropriate, and AES-GCM nonces.
- Never reuse an AES-GCM nonce with the same key.
- Use a sufficiently high PBKDF2 iteration count and a supported cryptographic hash such as SHA-256.
- Treat a six-digit PIN as a short-lived routing identifier, not as a high-entropy encryption key.
- Keep the encryption secret separate from the public invitation URL wherever possible.
- Ensure session revocation terminates or invalidates existing authorized connections, not just new HTTP requests.
- Avoid placing secrets in URLs, application logs, analytics, or error reports.
- Use a strict Content Security Policy and safe DOM APIs to prevent untrusted message content from executing as HTML or script.

**Important:** Client-side encryption is not automatically equivalent to a fully verified end-to-end encryption protocol. A production audit should confirm that the server cannot replace client code to capture keys, that key distribution is secure, and that participants connect to the intended session.

### Privacy Boundaries

LightThreads does not intentionally store chat messages in a database or write message history to disk. However, zero application-level persistence does not guarantee that every component leaves zero traces.

- The operating system may retain executable, process, or network metadata.
- Cloudflare and other network infrastructure may process connection metadata according to their configurations and policies.
- Browser memory, extensions, screenshots, and developer tools may expose messages.
- A host or guest can manually copy or record message content.
- Unexpected process termination clears in-memory server state but may not erase all traces from the operating system or connected infrastructure.

LightThreads is therefore best described as a **non-persistent, client-encrypted messaging application**, not a guarantee of complete anonymity or forensic untraceability.

## Technology Stack

| Component | Technology | Responsibility |
|---|---|---|
| Backend | C# and ASP.NET Core | HTTP endpoints, session lifecycle, and request handling |
| Real-Time Communication | ASP.NET Core SignalR | Connection management and message relay |
| Session Storage | `ConcurrentDictionary` | In-memory room and session state |
| Client-Side Cryptography | Web Crypto API | Local key derivation, encryption, and decryption |
| Encryption | AES-256-GCM | Authenticated message encryption |
| Key Derivation | PBKDF2 | Local derivation of cryptographic keys |
| Public Connectivity | Cloudflare Tunnel (`cloudflared`) | HTTPS access to the host's local application |
| Deployment | Self-contained Windows application | Simplified distribution without requiring a .NET SDK |

## Configuration and Operational Notes

### Port Configuration

The default local port is `5080`. Ensure the application and tunnel use the same configured endpoint.

### Cloudflare Tunnel

The application integrates with `cloudflared` to establish public HTTPS connectivity without manual router port forwarding. A successful tunnel does not, by itself, guarantee a secure session. Verify tunnel configuration, application endpoint restrictions, and secret handling.

### Client IP Extraction

When deployed behind Cloudflare, the application can use `CF-Connecting-IP` to identify the originating client IP. Only trust this header when the request arrives through a trusted proxy configuration. Do not accept arbitrary client-supplied forwarding headers as authoritative IP addresses.

### Session Cleanup

Session and room state should be removed when a session expires, the host closes it, or an invitation is revoked. The application should also handle disconnections and stale SignalR connections to avoid retaining unnecessary in-memory state.

## Project Status

LightThreads is a host-controlled, browser-based private messaging project built around temporary sessions, client-side encryption, and automated public connectivity.

The project provides a Windows application download path and a source-code development path. Security claims and implemented features should be verified against the current source code and published release.

## Contributing

Contributions, bug reports, and security improvements are welcome.

When reporting an issue:

- Include the operating system and .NET runtime or SDK version.
- Describe the steps required to reproduce the problem.
- Remove private URLs, PINs, encryption secrets, and other sensitive session details from logs and screenshots.
- Avoid publishing exploitable security vulnerabilities in public issues before coordinating responsible disclosure with the maintainers.

For security-sensitive reports, contact the repository maintainer through a verified private reporting channel.

## License

LightThreads is distributed under the MIT License.

See [`LICENSE`](LICENSE) for the complete license text.
