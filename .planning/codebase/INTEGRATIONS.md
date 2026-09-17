# External Integrations

**Analysis Date:** 2026-08-28

## APIs & External Services

**AWS SQS (source queue):**
- Amazon SQS - polled as the source of messages to redrive
  - SDK/Client: `AWSSDK.SQS` 4.0.100.11 (`Amazon.SQS.AmazonSQSClient`), wrapped in `Projects/AWSRedrive/AwsQueueClient.cs`
  - Auth: per-entry `AccessKey`/`SecretKey`, named `Profile`, or default AWS SDK credential chain (env vars / instance profile / shared credentials file) — none read directly from `.env`
  - Long-polls via `ReceiveMessageAsync` (20s wait), deletes via `DeleteMessageAsync`, and reads the `RedrivePolicy` queue attribute via `GetQueueAttributesAsync` to auto-discover the DLQ URL
  - Supports custom `ServiceUrl` override to point at an SQS emulator instead of real AWS (`Projects/AWSRedrive/AwsQueueClient.cs`). `Init()` sets `ServiceURL` *or* `RegionEndpoint`, never both, so `Region` is ignored once `ServiceUrl` is set — the SDK may still need `AWS_REGION` for signing. The integration suite exercises this path against Floci (`integration-infrastructure/`).

**Generic HTTP(S) / AWS API Gateway (redrive target):**
- Any HTTP(S) backend endpoint - target of "redrive" delivery
  - SDK/Client: `RestSharp` 114.0.0, implemented in `Projects/AWSRedrive/HttpMessageProcessor.cs`
  - Auth: `x-api-key` header (`AwsGatewayToken`, for AWS Gateway-fronted services), raw `Authorization` header (`AuthToken`), or HTTP Basic Auth (`BasicAuthUserName`/`BasicAuthPassword`) via `RestSharp.Authenticators.HttpBasicAuthenticator`
  - Supports GET (query-param unwrap of JSON body), POST, PUT, DELETE methods; optional TLS certificate validation bypass (`IgnoreCertificateErrors`)
  - Optionally unpacks an AWS SNS message envelope (`Projects/AWSRedrive/Models/SnsEnvelope.cs`) and forwards SNS `MessageAttributes` as HTTP headers (`UnpackAttributesAsHeaders`)

**Apache Kafka (redrive target):**
- Kafka cluster - alternate redrive delivery target
  - SDK/Client: `Confluent.Kafka` 2.15.0, implemented in `Projects/AWSRedrive/KafkaMessageProcessor.cs`
  - Auth: none built in beyond `KafkaBootstrapServers`/`KafkaClientId` config; relies on librdkafka defaults (no SASL/TLS config surfaced in `ConfigurationEntry`)
  - Producer configured with `Acks.All`, optional Snappy compression (`UseKafkaCompression`)

**PowerShell script execution (redrive target):**
- Local PowerShell scripts - alternate redrive delivery target (not a network integration, but an external process/runtime integration)
  - SDK/Client: `Microsoft.PowerShell.SDK` 7.6.5 embedded runtime (`System.Management.Automation`), implemented in `Projects/AWSRedrive/PowershellMessageProcessor.cs`
  - Invokes a user-provided `.ps1` file path (`RedriveScript`), passing message content and attributes as script parameters

## Data Storage

**Databases:**
- None. The application is stateless with respect to persistent storage; all state is in-memory (`Projects/AWSRedrive/MetricsStore.cs`) or read from local config files.

**File Storage:**
- Local filesystem only:
  - `config.json` - per-queue redrive configuration, read/re-read by `Projects/AWSRedrive/ConfigurationReader.cs` and watched by `Projects/AWSRedrive/ConfigurationChangeManager.cs`
  - `appsettings.json` - application-wide settings, bound via `Microsoft.Extensions.Configuration`
  - `logs/awsredrive.log` - rolling JSON log file (daily archive, 7 files retained), configured in each project's `NLog.config`
  - PowerShell `.ps1` scripts referenced by `RedriveScript` config entries

**Caching:**
- None (in-memory metrics only; no Redis/Memcached).

## Authentication & Identity

**Auth Provider:**
- None (custom, per-integration credentials only — no OAuth/OIDC/SSO provider integration)
- Outbound-only auth: AWS credentials for SQS (see above), and per-endpoint auth (API key header, bearer/opaque `Authorization` header, or Basic Auth) for HTTP redrive targets, configured per `ConfigurationEntry` in `config.json`
- The dashboard's own HTTP endpoints (`Projects/AWSRedrive/DashboardServer.cs`) have no authentication/authorization layer — `/`, `/api/status`, `/api/stream`, `/api/loglevel/{alias}` (GET/POST), `/health`, `/api/health` are all unauthenticated

## Monitoring & Observability

**Error Tracking:**
- None (no Sentry/Application Insights/Datadog integration). Errors are logged via NLog and surfaced in the in-memory `MetricsStore` (`Projects/AWSRedrive/MetricsStore.cs`, `Projects/AWSRedrive/MetricsSettingsProvider.cs`) and exposed through the dashboard `/api/status` endpoint.

**Logs:**
- NLog 6.2.0, JSON-layout structured logs written to console and to `${basedir}/logs/awsredrive.log` (daily rolling, 7-file retention) — `Projects/AWSRedrive/NLog.config`, `Projects/AWSRedrive.console/NLog.config`, `Projects/AWSRedrive.LinuxService/NLog.config`
- Per-queue log level is configurable at runtime via the dashboard API (`POST /api/loglevel/{alias}?level=...`) and stored per `ConfigurationEntry.LogLevel`, managed by `Projects/AWSRedrive/EntryLogger.cs`
- ASP.NET Core/Kestrel logs for the dashboard server are routed through NLog (`builder.Host.UseNLog()` in `DashboardServer.cs`)

## CI/CD & Deployment

**Hosting:**
- Self-hosted: runs as a Linux systemd service (`AWSRedrive.LinuxService`) or standalone console process (`AWSRedrive.console`), or as a Docker container built from `Dockerfile.image` (base image `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`, exposing port 5000)
- No cloud platform (AWS ECS/Lambda/etc.) deployment config found in-repo; AWS is used only as an SQS client, not as a hosting target

**CI Pipeline:**
- None detected in-repo (no `.github/workflows`, no `azure-pipelines.yml`, no `.gitlab-ci.yml`). Build/test/publish automation is provided via `Makefile` targets (`test`, `console`, `service`, `docker-build-*`, `image`, `image-push`) intended to be run manually or wired into an external CI system.
- `.gitignore` excludes `.sonarqube/` and `sonar*.*`, and the unit test project sets `SonarQubeTestProject=true`, indicating SonarQube analysis is expected to run in some external pipeline, though no pipeline config is present in this repo.

## Environment Configuration

**Required env vars:**
- None required directly by the application. AWS credential env vars (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_PROFILE`, `AWS_REGION`, etc.) are only consulted indirectly, by the default AWS SDK credential chain, when a `config.json` entry does not specify explicit `AccessKey`/`SecretKey`/`Profile`.
- `TARGETPLATFORM`/`BUILDPLATFORM`/`SKIP_TESTS` are Docker build ARGs used in `Dockerfile`/`Dockerfile.image`, not application runtime env vars.

**Secrets location:**
- Per-queue AWS credentials and redrive-endpoint auth tokens/passwords are stored in plaintext in `config.json` (`AccessKey`, `SecretKey`, `AuthToken`, `BasicAuthPassword`, `AwsGatewayToken`) — no secrets manager, vault, or encryption-at-rest integration is used. `config.json` is not listed in `.gitignore`, so operators must exclude the deployed/populated version manually.

## Webhooks & Callbacks

**Incoming:**
- None. The dashboard exposes read/control HTTP endpoints (status, SSE stream, health, log-level control) but does not receive inbound webhooks from external services — `Projects/AWSRedrive/DashboardServer.cs`.

**Outgoing:**
- Each SQS message consumed is "redriven" (pushed) to exactly one configured outbound target per queue entry: an HTTP(S) endpoint, a Kafka topic, or a local PowerShell script (see APIs & External Services above). This redrive push is the application's core outgoing integration, implemented via `Projects/AWSRedrive/Factories/MessageProcessorFactory.cs` selecting between `HttpMessageProcessor`, `KafkaMessageProcessor`, and `PowerShellMessageProcessor`.

---

*Integration audit: 2026-08-28*
