# Publishing and deploying Godrej WMS

## What gets published

The solution has one deployable application, `GodrejWMS.Web` (Blazor Server). It runs the UI and
the business logic (Application + Infrastructure layers) in the same process, and talks to MySQL
directly. There is currently **no separate API project**, so every target below publishes the same
app; they differ only in target OS/runtime and whether the .NET runtime is bundled.
`scripts/publish.sh` and `scripts/publish.ps1` take a target argument so another one (for example
`api`) can be added later without changing how you call them.

## Publish

From the repository root:

```bash
scripts/publish.sh ui                 # framework-dependent, portable, ~34 MB -> publish/ui
scripts/publish.sh ui-linux           # framework-dependent, fixed to Linux x64, ~33 MB -> publish/ui-linux-x64
scripts/publish.sh ui linux-x64       # self-contained for that runtime, ~145 MB -> publish/ui (no .NET needed on the server)
```

Windows (PowerShell): `.\scripts\publish.ps1 ui`, `.\scripts\publish.ps1 ui-linux`, or
`.\scripts\publish.ps1 ui win-x64`.

`ui` and `ui-linux` both need the **.NET 10 ASP.NET Core runtime** already installed on the server
("framework-dependent"). The difference:
- **`ui`** is portable — it runs as `dotnet GodrejWMS.Web.dll` on whatever OS has the runtime.
- **`ui-linux`** is fixed to `linux-x64` at publish time, so it also gets a native `./GodrejWMS.Web`
  apphost and any Linux-specific native assets. Use this one when you know the server is Linux x64
  and want to run the app directly rather than via `dotnet ... .dll`.

Add a runtime identifier as a second argument (`linux-x64`, `win-x64`, `osx-arm64`, ...) to the
`ui` target for a **self-contained** build instead, which bundles the .NET runtime itself so the
server needs nothing pre-installed (much larger output).

Each target wraps `dotnet publish src/GodrejWMS.Web -p:PublishProfile=<name>`. The profiles live in
`src/GodrejWMS.Web/Properties/PublishProfiles/` (`UI.pubxml`, `UILinux.pubxml`), publish in Release
mode to their own `publish/<target>/` folder, and empty that folder first so files removed from the
project never linger. `publish/` is git-ignored.

> **Profile filenames can't contain a `.` before `.pubxml`** (e.g. `UI.linux-x64.pubxml`) — the SDK
> silently falls back to a different profile instead of erroring, which is how `UILinux.pubxml`
> got its name instead of `UI.linux-x64.pubxml`. Found by checking the actual published output
> (RID, apphost) rather than trusting the build log's final path line, which repeated the old
> profile's path even though nothing was actually written there.

## Run

```bash
cd publish/ui && ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet GodrejWMS.Web.dll      # ui: framework-dependent, portable
cd publish/ui-linux-x64 && ASPNETCORE_URLS=http://0.0.0.0:5000 ./GodrejWMS.Web     # ui-linux: framework-dependent, Linux x64
cd publish/ui && ASPNETCORE_URLS=http://0.0.0.0:5000 ./GodrejWMS.Web              # ui + RID: self-contained
```

On IIS the generated `web.config` hosts it (ASP.NET Core Hosting Bundle required; `web.config` is
only produced for Windows-hostable output, so it's absent from `ui-linux`).
The app applies EF migrations and seeds reference data and the default users on startup.

## Run as a systemd service (Ubuntu)

Since the service invokes `dotnet <path>/GodrejWMS.Web.dll` directly (not the native apphost), it
works the same regardless of which framework-dependent target (`ui` or `ui-linux`) is deployed —
the RID only changes the apphost/native assets, not whether `dotnet` can load the managed DLL.

`/etc/systemd/system/godrejwms.service`:
```ini
[Unit]
Description=Godrej WMS
After=network.target

[Service]
WorkingDirectory=/home/ubuntu/GCPL/warehouse-gate/Web
ExecStart=/usr/bin/dotnet /home/ubuntu/GCPL/warehouse-gate/Web/GodrejWMS.Web.dll
Restart=always
RestartSec=10
SyslogIdentifier=GodrejWMS
User=ubuntu
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5003
# Secrets (DB connection string, default admin password) go in an EnvironmentFile, not here,
# so they aren't sitting in a 644 unit file readable by any local user:
EnvironmentFile=/home/ubuntu/GCPL/warehouse-gate/godrejwms.env

[Install]
WantedBy=multi-user.target
```

`AmbientCapabilities=CAP_NET_BIND_SERVICE` is only needed to bind ports below 1024; harmless to
keep, but not required for port 5003.

`/home/ubuntu/GCPL/warehouse-gate/godrejwms.env` (create with `chmod 600`, owned by the service user):
```
ConnectionStrings__Default=Server=...;Port=3306;Database=GodrejWMS;User=...;Password=...;
DefaultAdmin__Email=admin@yourcompany.com
DefaultAdmin__Password=<a real password, not the seeded default>
```

Deploying a new build (stop first so files aren't overwritten while the process has them open):
```bash
sudo systemctl stop godrejwms
sha256sum -c ui-linux-x64.tar.gz.sha256                                   # must print OK before extracting
mkdir -p /home/ubuntu/GCPL/warehouse-gate/Web
tar xzf ui-linux-x64.tar.gz --strip-components=1 -C /home/ubuntu/GCPL/warehouse-gate/Web
sudo systemctl daemon-reload                                               # only needed after editing the unit file itself
sudo systemctl start godrejwms
sudo systemctl status godrejwms
journalctl -u godrejwms -f                                                 # tail logs; Ctrl+C to stop watching
curl -I http://localhost:5003/Account/Login                                # expect 200
```

## Configure for the server (do this before going live)

`appsettings.json` ships development defaults (local MySQL as `root` with no password, and a
well-known default admin password). Override them per environment with environment variables
rather than editing the published files:

| Setting | Environment variable |
| --- | --- |
| Database | `ConnectionStrings__Default="Server=...;Port=3306;Database=GodrejWMS;User=...;Password=...;"` |
| First admin account (only used when the user does not exist yet) | `DefaultAdmin__Email`, `DefaultAdmin__Password` |
| Listen address | `ASPNETCORE_URLS` |
| Environment name | `ASPNETCORE_ENVIRONMENT` (defaults to `Production`) |

Also change the default admin password after the first login and, if you keep the seeded
`supervisor@` and `operator@` sample accounts, change or remove them.

Production notes:
- Serve over HTTPS (reverse proxy or Kestrel certificate). The app redirects HTTP to HTTPS when it
  can work out the HTTPS port, and logs a warning otherwise; that warning is harmless behind a
  proxy that terminates TLS.
- ASP.NET Core Data Protection keys are stored in the default location and logged as unencrypted
  ("No XML encryptor configured"). On a server that restarts, or with more than one instance,
  configure a persistent, protected key store so users are not signed out and cookies stay valid.
- Blazor Server keeps a live connection per user, so a reverse proxy must allow WebSockets.
- Logs are written to `Logs/godrejwms-<date>.log` next to the app (14 days kept).

## AI Assistant (optional)

The admin-only **AI Assistant** report (`/reports/ai-assistant`) routes plain-English questions to
the existing operational report queries using a locally-hosted, open-source model via
[Ollama](https://ollama.com) — free, no API key, and the data never leaves the server. It's
optional: if Ollama isn't running, the page shows a friendly "couldn't reach the AI assistant"
message and every other feature keeps working.

Setup:
```bash
brew install ollama          # macOS; see ollama.com/download for Linux/Windows
ollama pull llama3.2:3b      # ~2 GB, good balance of speed/quality for this feature
ollama serve                 # or `brew services start ollama` to run it persistently
```

Config (`appsettings.json` / environment variables), same override pattern as the database above:

| Setting | Environment variable | Default |
| --- | --- | --- |
| Ollama server URL | `Ai__OllamaBaseUrl` | `http://localhost:11434` |
| Model name | `Ai__Model` | `llama3.2:3b` |

The model only ever picks from the app's existing, known report kinds and filter fields (see
`ReportKind` in `GodrejWMS.Application.Features.Reports.Core`) — it never generates or runs SQL, so
there's no injection surface from admin questions reaching the database.

## Checked

The `ui` output was started on its own port in Production mode: the login page returned 200, and
the app's CSS, JavaScript, Blazor framework and icon files all returned 200. The self-contained
option was built for `osx-arm64` and the default output restored afterwards. The `ui-linux` output
was verified statically (its apphost is an ELF Linux x86-64 binary, and its `runtimeconfig.json`
targets the shared `Microsoft.NETCore.App`/`Microsoft.AspNetCore.App` 10.0.0 frameworks rather than
bundling them) since this machine can't execute a Linux binary to boot-test it directly.
