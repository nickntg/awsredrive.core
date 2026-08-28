# Technology Stack

**Analysis Date:** 2026-08-28

## Languages

**Primary:**
- C# 12 (implicit, via `net8.0` TargetFramework) - all production code in `Projects/AWSRedrive`, `Projects/AWSRedrive.console`, `Projects/AWSRedrive.LinuxService`
- PowerShell (consumed, not authored, by the app) - `Projects/AWSRedrive/PowershellMessageProcessor.cs` executes user-supplied `.ps1` redrive scripts via `System.Management.Automation`

**Secondary:**
- HTML/CSS/vanilla JS - `Projects/AWSRedrive/dashboard.html` (single static page served by the built-in dashboard web server)
- JSON - configuration files (`config.json`, `appsettings.json`) and NLog JSON log layout
- XML - `Projects/AWSRedrive/NLog.config`, `Projects/AWSRedrive.console/NLog.config`, `Projects/AWSRedrive.LinuxService/NLog.config`, `Solutions/AWSRedrive.Core.sln`
- Makefile/shell - `Makefile` (build/test/docker orchestration, macOS+Linux+Windows aware)

## Runtime

**Environment:**
- .NET 8.0 (`net8.0` target framework across all projects)
- `Projects/AWSRedrive/AWSRedrive.csproj` (class library, core logic)
- `Projects/AWSRedrive.console/AWSRedrive.console.csproj` (console host, `OutputType=Exe`)
- `Projects/AWSRedrive.LinuxService/AWSRedrive.LinuxService.csproj` (Worker Service SDK, `Microsoft.NET.Sdk.Worker`, runs as systemd service via `Microsoft.Extensions.Hosting.Systemd`)
- Self-contained, single-file publish (`PublishSingleFile=true`) targeting `linux-x64`/`linux-arm64`/`osx-x64`/`osx-arm64`/`win-x64` (see `Makefile` runtime auto-detection and `Dockerfile`/`Dockerfile.image` `TARGETPLATFORM` handling)

**Package Manager:**
- NuGet (via `dotnet` CLI / `PackageReference` in `.csproj` files)
- Lockfile: not present (no `packages.lock.json` found) - versions pinned directly in each `.csproj`

## Frameworks

**Core:**
- ASP.NET Core minimal APIs (`Microsoft.AspNetCore.App` framework reference) - powers the embedded dashboard web server, `Projects/AWSRedrive/DashboardServer.cs` (Kestrel via `WebApplication.CreateBuilder()`)
- Microsoft.Extensions.DependencyInjection 8.0.1 - composition root in `Projects/AWSRedrive/DI/Injector.cs`
- Microsoft.Extensions.Hosting 8.0.1 / Hosting.Systemd 8.0.1 - generic host + systemd integration for the Linux service, `Projects/AWSRedrive.LinuxService/Program.cs`
- Microsoft.Extensions.Configuration(.Json) 8.0.0 - binds `appsettings.json` into `Projects/AWSRedrive/Models/AppSettings.cs`
- FluentValidation 12.1.1 - validates `ConfigurationEntry` objects, `Projects/AWSRedrive/Validations/ConfigurationEntryValidator.cs`

**Testing:**
- xUnit 2.9.2 (+ `xunit.runner.visualstudio` 2.8.2) - `Tests/AWSRedrive.Tests.Unit`, `Tests/AWSRedrive.Test.Integration`
- FakeItEasy 8.3.0 - mocking library used throughout `Tests/AWSRedrive.Tests.Unit/*.cs`
- Microsoft.NET.Test.Sdk 17.11.1

**Build/Dev:**
- `dotnet` CLI (build/test/publish) orchestrated via `Makefile` (targets: `run`, `watch`, `test`, `console`, `service`, `all`, `docker-*`, `image`, `sign`)
- Docker multi-stage builds - `Dockerfile` (scratch-based, artifact extraction) and `Dockerfile.image` (runnable container image, `mcr.microsoft.com/dotnet/runtime-deps:8.0-noble-chiseled` base)
- SonarQube integration - `Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj` sets `SonarQubeTestProject=true`; `.gitignore` excludes `.sonarqube/` and `sonar*.*`

## Key Dependencies

**Critical:**
- `AWSSDK.SQS` 4.0.3.7 - core SQS polling/deleting/queue-attribute client, `Projects/AWSRedrive/AwsQueueClient.cs`
- `Confluent.Kafka` 2.14.2 - Kafka producer for redrive-to-Kafka mode, `Projects/AWSRedrive/KafkaMessageProcessor.cs`
- `RestSharp` 114.0.0 - HTTP client for redrive-to-HTTP(S) mode, `Projects/AWSRedrive/HttpMessageProcessor.cs`
- `Microsoft.PowerShell.SDK` 7.4.6 (+ `Microsoft.PowerShell.Commands.Diagnostics`, `Microsoft.WSMan.Management` 7.4.6) - embeds PowerShell runtime for redrive-to-script mode, `Projects/AWSRedrive/PowershellMessageProcessor.cs`
- `FluentValidation` 12.1.1 - configuration entry validation
- `Newtonsoft.Json` 13.0.4 - configuration file parsing (`ConfigurationReader.cs`) and SNS envelope parsing (`HttpMessageProcessor.cs`)

**Infrastructure:**
- `NLog` 6.1.3 / `NLog.Web.AspNetCore` 6.1.3 / `NLog.Extensions.Logging` 6.1.3 - structured JSON logging to console + rolling file, configured per-project via `NLog.config`
- `System.Text.Json` (BCL) - dashboard API JSON responses in `DashboardServer.cs`

## Configuration

**Environment:**
- No `.env` files present in the repo.
- AWS credentials resolved via (in order of precedence per `ConfigurationEntry`): explicit `AccessKey`/`SecretKey` in `config.json`, named `Profile` (AWS credentials file), or default AWS SDK credential chain - `Projects/AWSRedrive/AwsQueueClient.cs`
- Application-wide settings loaded from `appsettings.json` (Dashboard enable/port/refresh interval, Metrics enable/interval, `DefaultLogLevel`) - bound into `Projects/AWSRedrive/Models/AppSettings.cs`
- Per-queue redrive configuration loaded from `config.json` (array of `ConfigurationEntry` objects: queue URL, region, redrive target - URL/script/Kafka topic, auth, timeouts, log level) - `Projects/AWSRedrive/ConfigurationReader.cs`
- `config.json` is watched/re-read at runtime for hot config changes (no restart required) - `Projects/AWSRedrive/ConfigurationChangeManager.cs`
- Each executable (`AWSRedrive.console`, `AWSRedrive.LinuxService`) ships its own copies of `appsettings.json`, `config.json`, `NLog.config` next to the binary (`CopyToOutputDirectory`)

**Build:**
- `Solutions/AWSRedrive.Core.sln` - solution file referencing all 5 projects
- `Solutions/AWSRedrive.Core.sln.DotSettings` - ReSharper/Rider settings
- Per-project `.csproj` files define target framework, package references, and output copy rules

## Platform Requirements

**Development:**
- .NET 8 SDK (`mcr.microsoft.com/dotnet/sdk:8.0` used in Docker build stages)
- `make` (GNU Make) for the convenience build/test/docker targets in `Makefile`
- Docker (optional, for cross-platform/reproducible builds and containerized runs)
- macOS-specific `sign` target in `Makefile` for code-signing local builds (Apple codesign, unrelated to core runtime deps)

**Production:**
- Self-contained single-file binaries deployed per-platform (`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`)
- Linux: runs as a systemd-managed background service (`AWSRedrive.LinuxService`, uses `Microsoft.Extensions.Hosting.Systemd`)
- Any platform: runs as a foreground console app (`AWSRedrive.console`) with Ctrl+C / process-exit graceful shutdown
- Container images published via `Dockerfile.image`, based on `mcr.microsoft.com/dotnet/runtime-deps:8.0-noble-chiseled` (minimal/chiseled Ubuntu Noble), exposing port 5000 for the embedded dashboard

---

*Stack analysis: 2026-08-28*
