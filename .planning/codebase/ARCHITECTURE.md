<!-- refreshed: 2026-08-28 -->
# Architecture

**Analysis Date:** 2026-08-28

## System Overview

```text
┌─────────────────────────────────────────────────────────────────────┐
│                          Host / Entry Point                          │
├────────────────────────────────┬──────────────────────────────────┤
│  AWSRedrive.console (Program)  │  AWSRedrive.LinuxService (Worker)  │
│  `Projects/AWSRedrive.console/` │  `Projects/AWSRedrive.LinuxService/`│
│  Console app, Ctrl+C loop        │  Generic Host BackgroundService   │
└────────────────────────────────┴──────────────────────────────────┘
                 │                              │
                 └──────────────┬───────────────┘
                                ▼
                  ┌──────────────────────────────┐
                  │     DI / Injector             │
                  │  `Projects/AWSRedrive/DI/Injector.cs` │
                  └──────────────┬───────────────┘
                                ▼
                  ┌──────────────────────────────┐
                  │        Orchestrator            │
                  │  `Projects/AWSRedrive/Orchestrator.cs` │
                  │  Polls config every 60s,        │
                  │  reconciles running processors  │
                  └──────────────┬───────────────┘
                                ▼
       ┌────────────────────────┴─────────────────────────┐
       ▼                                                     ▼
┌───────────────────────┐                    ┌──────────────────────────┐
│ ConfigurationReader     │                    │ ConfigurationChangeManager│
│ + ConfigurationEntry    │◄──────────────────►│ (diff/add/remove         │
│ Validator (config.json) │                    │  QueueProcessors)         │
│ `ConfigurationReader.cs`│                    │ `ConfigurationChangeManager.cs`│
└───────────────────────┘                    └──────────────┬───────────┘
                                                              ▼
                                        ┌───────────────────────────────┐
                                        │   QueueProcessor (1 per entry) │
                                        │  `Projects/AWSRedrive/QueueProcessor.cs`│
                                        │  Long-running Task poll loop   │
                                        └───────┬───────────┬───────────┘
                                                 ▼           ▼
                                  ┌──────────────────┐  ┌──────────────────────┐
                                  │  IQueueClient      │  │  IMessageProcessor     │
                                  │  (AwsQueueClient →  │  │  (Http/Kafka/PowerShell)│
                                  │  SQS SDK)           │  │  `HttpMessageProcessor.cs`│
                                  │  `AwsQueueClient.cs`│  │  `KafkaMessageProcessor.cs`│
                                  └──────────────────┘  │  `PowershellMessageProcessor.cs`│
                                                          └──────────────────────┘
                                                 │
                                                 ▼
                                  ┌──────────────────────────────┐
                                  │  MetricsStore (static, per-alias)│
                                  │  `Projects/AWSRedrive/MetricsStore.cs`│
                                  └──────────────────────────────┘
                                                 ▲
                                                 │ read
                  ┌──────────────────────────────┴──────────────────────┐
                  │             DashboardServer (ASP.NET Core minimal API)│
                  │  `Projects/AWSRedrive/DashboardServer.cs`             │
                  │  Serves dashboard.html + /api/status + SSE stream     │
                  └───────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Console entry point | Bootstraps config, DI, starts orchestrator + dashboard, blocks on Ctrl+C/process exit | `Projects/AWSRedrive.console/Program.cs` |
| Linux service entry point | Bootstraps `Microsoft.Extensions.Hosting` generic host, registers `Worker` as hosted service | `Projects/AWSRedrive.LinuxService/Program.cs` |
| Worker (BackgroundService) | Starts/stops orchestrator and dashboard under the generic host lifecycle | `Projects/AWSRedrive.LinuxService/Worker.cs` |
| Injector | Central composition root; registers all services with `Microsoft.Extensions.DependencyInjection` | `Projects/AWSRedrive/DI/Injector.cs` |
| Orchestrator | Top-level supervisor loop; polls for config changes every 60s and stops all processors on shutdown | `Projects/AWSRedrive/Orchestrator.cs` |
| ConfigurationReader | Reads and validates `config.json` into `List<ConfigurationEntry>` | `Projects/AWSRedrive/ConfigurationReader.cs` |
| ConfigurationEntryValidator | FluentValidation rules for a configuration entry (exactly one redrive target, mutually exclusive HTTP verbs) | `Projects/AWSRedrive/Validations/ConfigurationEntryValidator.cs` |
| ConfigurationChangeManager | Diffs desired config vs running processors; starts new `QueueProcessor`s, stops removed ones | `Projects/AWSRedrive/ConfigurationChangeManager.cs` |
| QueueProcessor | Per-queue worker; owns a background polling `Task`, converts messages, invokes the message processor, deletes on success, tracks metrics | `Projects/AWSRedrive/QueueProcessor.cs` |
| AwsQueueClient | Wraps `AmazonSQSClient`; receive/delete messages, resolve DLQ URL from `RedrivePolicy` | `Projects/AWSRedrive/AwsQueueClient.cs` |
| HttpMessageProcessor | Redrives a message to an HTTP(S) endpoint via RestSharp (GET/POST/PUT/DELETE, auth headers, SNS attribute unpacking) | `Projects/AWSRedrive/HttpMessageProcessor.cs` |
| KafkaMessageProcessor | Redrives a message to a Kafka topic via Confluent.Kafka producer | `Projects/AWSRedrive/KafkaMessageProcessor.cs` |
| PowerShellMessageProcessor | Redrives a message by executing a local PowerShell script with the message as a parameter | `Projects/AWSRedrive/PowershellMessageProcessor.cs` |
| MessageProcessorFactory | Chooses which `IMessageProcessor` to use per config entry (`RedriveUrl` > `RedriveScript` > `RedriveKafkaTopic`) | `Projects/AWSRedrive/Factories/MessageProcessorFactory.cs` |
| QueueClientFactory / QueueProcessorFactory | Construct `IQueueClient` / `IQueueProcessor` instances | `Projects/AWSRedrive/Factories/QueueClientFactory.cs`, `Projects/AWSRedrive/Factories/QueueProcessorFactory.cs` |
| EntryLogger | Per-entry (per-alias, optionally per-message) logger with a runtime-adjustable minimum log level; wraps NLog | `Projects/AWSRedrive/EntryLogger.cs` |
| MetricsStore | Static, process-wide `ConcurrentDictionary<string, QueueMetrics>` keyed by alias | `Projects/AWSRedrive/MetricsStore.cs` |
| MetricsSettingsProvider | Adapter exposing `AppSettings.Metrics` as `IMetricsSettings` | `Projects/AWSRedrive/MetricsSettingsProvider.cs` |
| DashboardServer | Self-hosted ASP.NET Core minimal API (Kestrel) serving a static dashboard page, JSON status endpoint, SSE stream, and log-level control endpoints | `Projects/AWSRedrive/DashboardServer.cs` |

## Pattern Overview

**Overall:** Layered service-worker architecture with a plugin-style strategy pattern for outbound delivery ("redrive") and a supervisor/reconciliation loop for lifecycle management. Two thin hosting shells (console, Linux service) wrap one shared core library.

**Key Characteristics:**
- Single shared class library (`Projects/AWSRedrive`) contains all domain logic; the two executables (`AWSRedrive.console`, `AWSRedrive.LinuxService`) only differ in process-hosting model.
- Configuration-driven fan-out: one `ConfigurationEntry` in `config.json` → one `QueueProcessor` running its own background `Task`.
- Reconciliation loop (`Orchestrator.StartProcessing`) re-reads `config.json` every 60 seconds and diffs against running processors (add/remove), enabling zero-restart configuration changes.
- Strategy pattern for message delivery: `IMessageProcessor` implementations (`HttpMessageProcessor`, `KafkaMessageProcessor`, `PowerShellMessageProcessor`) selected by `MessageProcessorFactory` based on which of `RedriveUrl` / `RedriveScript` / `RedriveKafkaTopic` is set.
- Dependency injection via `Microsoft.Extensions.DependencyInjection`, composed once in `Injector.Inject(...)`; the console app builds its own `ServiceProvider`, the Linux service integrates with the generic host's `IServiceCollection`.
- Dashboard is an embedded, independently startable ASP.NET Core app (own Kestrel instance, own port) that reads shared state (`MetricsStore`, `Orchestrator`, `IConfigurationReader`) rather than sharing a web pipeline with the core worker loop.

## Layers

**Hosting Layer:**
- Purpose: Process bootstrapping, lifecycle wiring, OS-specific concerns (Ctrl+C vs. generic host/systemd)
- Location: `Projects/AWSRedrive.console/`, `Projects/AWSRedrive.LinuxService/`
- Contains: `Program.cs` (both), `Worker.cs` (Linux service only, a `BackgroundService`)
- Depends on: Core library (`AWSRedrive`, `AWSRedrive.DI`, `AWSRedrive.Interfaces`, `AWSRedrive.Models`)
- Used by: Nothing (top of the stack)

**Composition Layer:**
- Purpose: Wires all interfaces to concrete implementations
- Location: `Projects/AWSRedrive/DI/Injector.cs`
- Contains: Static `Inject(...)` overloads for both self-hosted `ServiceProvider` and externally supplied `IServiceCollection`
- Depends on: All interfaces/implementations in the core library
- Used by: Hosting layer

**Orchestration Layer:**
- Purpose: Supervises the set of running queue processors and reacts to configuration changes
- Location: `Projects/AWSRedrive/Orchestrator.cs`, `Projects/AWSRedrive/ConfigurationChangeManager.cs`
- Contains: `Orchestrator` (owns the polling `Task` and processor list), `ConfigurationChangeManager` (diff/add/remove logic)
- Depends on: `IConfigurationReader`, `IQueueClientFactory`, `IMessageProcessorFactory`, `IQueueProcessorFactory`
- Used by: Hosting layer (`Program.cs`/`Worker.cs`), `DashboardServer` (for log-level control and status)

**Configuration Layer:**
- Purpose: Load and validate the `config.json` redrive definitions and `appsettings.json` app settings
- Location: `Projects/AWSRedrive/ConfigurationReader.cs`, `Projects/AWSRedrive/Validations/ConfigurationEntryValidator.cs`, `Projects/AWSRedrive/Models/AppSettings.cs`, `Projects/AWSRedrive/Models/ConfigurationEntry.cs`
- Contains: JSON deserialization (Newtonsoft.Json) and FluentValidation rules
- Depends on: Filesystem (`config.json`, `appsettings.json` in the working directory)
- Used by: Orchestration layer, `DashboardServer`

**Processing Layer:**
- Purpose: Per-queue message pump — receive, log, dispatch, delete, track metrics
- Location: `Projects/AWSRedrive/QueueProcessor.cs`
- Contains: The message loop (`ProcessMessageLoop`), log-level expiry timer, periodic metrics logging
- Depends on: `IQueueClient`, `IMessageProcessorFactory`, `EntryLogger`, `MetricsStore`, `IMetricsSettings`
- Used by: `ConfigurationChangeManager` (creates/starts/stops instances)

**Integration Layer:**
- Purpose: Talk to external systems (SQS, HTTP endpoints, Kafka, PowerShell host)
- Location: `Projects/AWSRedrive/AwsQueueClient.cs`, `Projects/AWSRedrive/HttpMessageProcessor.cs`, `Projects/AWSRedrive/KafkaMessageProcessor.cs`, `Projects/AWSRedrive/PowershellMessageProcessor.cs`
- Contains: SDK/client wrappers implementing `IQueueClient` / `IMessageProcessor`
- Depends on: `AWSSDK.SQS`, `RestSharp`, `Confluent.Kafka`, `System.Management.Automation`
- Used by: Processing layer

**Observability Layer:**
- Purpose: Structured logging, in-memory metrics, and a live status dashboard
- Location: `Projects/AWSRedrive/EntryLogger.cs`, `Projects/AWSRedrive/MetricsStore.cs`, `Projects/AWSRedrive/MetricsSettingsProvider.cs`, `Projects/AWSRedrive/DashboardServer.cs`, `Projects/AWSRedrive/NLog.config`
- Contains: NLog wrapper with per-alias/per-message scoping, static process-wide metrics dictionary, ASP.NET Core minimal API dashboard
- Depends on: NLog, NLog.Web.AspNetCore, `Microsoft.AspNetCore.App` (via `FrameworkReference`)
- Used by: All layers above (via `EntryLogger`); `DashboardServer` reads `MetricsStore` and `IOrchestrator` directly

## Data Flow

### Primary Redrive Path

1. `Orchestrator.StartProcessing` wakes every 60s, calls `ConfigurationChangeManager.ReadChanges` (`Projects/AWSRedrive/Orchestrator.cs:56-70`)
2. `ConfigurationReader.ReadConfiguration` reads and validates `config.json` into `List<ConfigurationEntry>` (`Projects/AWSRedrive/ConfigurationReader.cs:20-38`)
3. `ConfigurationChangeManager` diffs against currently running `IQueueProcessor`s; for new/changed entries it creates an `IQueueClient` via `QueueClientFactory`, calls `Init()`, fetches the DLQ URL, then creates and starts an `IQueueProcessor` (`Projects/AWSRedrive/ConfigurationChangeManager.cs:96-135`)
4. `QueueProcessor.Start` spins up a dedicated long-running `Task` running `ProcessMessageLoop` (`Projects/AWSRedrive/QueueProcessor.cs:83-99`)
5. `ProcessMessageLoop` polls `IQueueClient.GetMessage()` (long-poll, 20s wait) (`Projects/AWSRedrive/QueueProcessor.cs:121-140`, `Projects/AWSRedrive/AwsQueueClient.cs:56-104`)
6. On receipt: metrics updated, message logged (Debug/Trace levels), `IMessageProcessorFactory.CreateMessageProcessor` selects `HttpMessageProcessor` / `KafkaMessageProcessor` / `PowerShellMessageProcessor` based on which redrive target is configured (`Projects/AWSRedrive/QueueProcessor.cs:186-200`, `Projects/AWSRedrive/Factories/MessageProcessorFactory.cs`)
7. Message processor delivers the payload to the target (HTTP call / Kafka produce / PowerShell script execution)
8. On success, `IQueueClient.DeleteMessage` removes the message from SQS; on failure, error metrics are recorded and the message remains in the queue for redelivery/DLQ (`Projects/AWSRedrive/QueueProcessor.cs:202-225`)

### Dashboard Status Flow

1. `DashboardServer.Start` builds and runs its own ASP.NET Core minimal API app on `AppSettings.Dashboard.Port` (`Projects/AWSRedrive/DashboardServer.cs:73-97`)
2. `GET /` serves cached (optionally gzip-precompressed) `dashboard.html` (`Projects/AWSRedrive/DashboardServer.cs:99-108`)
3. `GET /api/status` and `GET /api/stream` (Server-Sent Events) call `GetStatus()`, which merges `IConfigurationReader.ReadConfiguration()` (all entries) with `IOrchestrator.GetConfigurations()` (active/runtime entries, including `DlqUrl`) and `MetricsStore.GetOrCreate(alias)` per entry (`Projects/AWSRedrive/DashboardServer.cs:110-134,225-269`)
4. `POST /api/loglevel/{alias}` and `GET /api/loglevel/{alias}` delegate to `IOrchestrator.SetLogLevel` / `GetLogLevel`, which find the matching `QueueProcessor` by alias and mutate its `EntryLogger` (`Projects/AWSRedrive/DashboardServer.cs:143-183`, `Projects/AWSRedrive/Orchestrator.cs:98-120`)

**State Management:**
- Runtime state (list of active processors, per-processor config) lives in `Orchestrator._processors`, guarded by a private `lock` object; there is no external state store.
- Metrics live in the static `MetricsStore` (`ConcurrentDictionary<string, QueueMetrics>`), keyed by config alias, shared process-wide across all `QueueProcessor` instances and read directly by `DashboardServer`.
- Log level overrides are stored on the `EntryLogger` instance owned by each `QueueProcessor`, with a 30-minute auto-revert timer (`QueueProcessor.CheckLogLevelExpiry`).

## Key Abstractions

**IQueueClient:**
- Purpose: Abstracts the message-queue transport (currently only SQS) so `QueueProcessor` is transport-agnostic
- Examples: `Projects/AWSRedrive/Interfaces/IQueueClient.cs`, `Projects/AWSRedrive/AwsQueueClient.cs`
- Pattern: Interface + single concrete adapter, created per-`ConfigurationEntry` via `IQueueClientFactory`

**IMessageProcessor:**
- Purpose: Abstracts the redrive destination/protocol (strategy pattern)
- Examples: `Projects/AWSRedrive/Interfaces/IMessageProcessor.cs`, `Projects/AWSRedrive/HttpMessageProcessor.cs`, `Projects/AWSRedrive/KafkaMessageProcessor.cs`, `Projects/AWSRedrive/PowershellMessageProcessor.cs`
- Pattern: Selected at message-processing time (not cached) by `MessageProcessorFactory.CreateMessageProcessor`, based on which of `RedriveUrl`/`RedriveScript`/`RedriveKafkaTopic` is populated on the `ConfigurationEntry`

**IQueueProcessor:**
- Purpose: Represents one running "pipeline" (queue → processor) with its own lifecycle and log level
- Examples: `Projects/AWSRedrive/Interfaces/IQueueProcessor.cs`, `Projects/AWSRedrive/QueueProcessor.cs`
- Pattern: One instance per active `ConfigurationEntry`, managed in a `List<IQueueProcessor>` owned by `Orchestrator`

**ConfigurationEntry:**
- Purpose: The unit of configuration — one SQS source queue mapped to one redrive destination
- Examples: `Projects/AWSRedrive/Models/ConfigurationEntry.cs`
- Pattern: POJO bound from `config.json` via Newtonsoft.Json, validated by `ConfigurationEntryValidator` (FluentValidation), compared field-by-field (excluding `LogLevel`) in `ConfigurationChangeManager.ConfigurationsMatch`

**EntryLogger:**
- Purpose: Per-alias (optionally per-message) NLog wrapper with an independently adjustable minimum log level and `[CallerFilePath]`-derived logger name
- Examples: `Projects/AWSRedrive/EntryLogger.cs`
- Pattern: Not a static/singleton logger — one instance owned per `QueueProcessor`, cloned via `WithMessageId` for message-scoped correlation

## Entry Points

**AWSRedrive.console (Program.Main):**
- Location: `Projects/AWSRedrive.console/Program.cs`
- Triggers: Direct process launch (`dotnet AWSRedrive.console.dll` or self-contained single-file executable)
- Responsibilities: Load `appsettings.json`, call `Injector.Inject(appSettings)`, resolve `IOrchestrator` and start it, optionally start `DashboardServer`, block on `ManualResetEventSlim` until Ctrl+C/process-exit, then stop orchestrator and dashboard in order

**AWSRedrive.LinuxService (Program.Main → Worker):**
- Location: `Projects/AWSRedrive.LinuxService/Program.cs`, `Projects/AWSRedrive.LinuxService/Worker.cs`
- Triggers: systemd/service-manager launch via the .NET Generic Host (`Host.CreateDefaultBuilder`)
- Responsibilities: Same startup sequence as the console app but driven by `Worker : BackgroundService`, integrates with `IHostedService` lifecycle and NLog-via-host logging (`UseNLog()`)

**DashboardServer.Start:**
- Location: `Projects/AWSRedrive/DashboardServer.cs`
- Triggers: Called explicitly by the hosting layer when `AppSettings.Dashboard.Enabled` is true; runs its own independent ASP.NET Core/Kestrel instance on `Dashboard.Port`
- Responsibilities: Serve dashboard UI, JSON/SSE status endpoints, log-level control endpoints

## Architectural Constraints

- **Threading:** Each `QueueProcessor` runs its own dedicated `TaskCreationOptions.LongRunning` thread-pool task doing blocking, synchronous-over-async SQS polling (`.Result` calls in `AwsQueueClient`). The `Orchestrator` reconciliation loop runs on its own separate `Task` with a 1-second `Thread.Sleep` tick. The `DashboardServer` runs on its own Kestrel/ASP.NET Core thread pool. There is no shared thread pool budget management — with many active queues, the number of long-running blocking tasks grows linearly with `config.json` entries.
- **Global state:** `MetricsStore` (`Projects/AWSRedrive/MetricsStore.cs`) is a `static` class with a `static readonly ConcurrentDictionary` — process-wide, not scoped to DI container, making it a de facto singleton that outlives `Orchestrator.Stop()/Start()` cycles (metrics persist across processor restarts within the same process). `Injector.Container` is also a public static field.
- **Circular imports:** `Projects/AWSRedrive/Interfaces/IQueueProcessor.cs` has a redundant self-referential `using AWSRedrive.Interfaces;` inside the `AWSRedrive.Interfaces` namespace itself (harmless but notable).
- **Synchronous-over-async:** `AwsQueueClient.GetMessage()` and `GetDlqUrl()` call `.Result` on async SQS SDK calls instead of awaiting them, which blocks the calling thread for the duration of the (up to 20s) long-poll; `HttpMessageProcessor.SendRequest` and `KafkaMessageProcessor.ProcessMessage` also block on `.Result`.
- **Locking:** `Orchestrator` and `QueueProcessor` share mutable state (`_processors`, log level fields) guarded by simple `lock` (Monitor) objects — no async-aware synchronization primitives; long-held locks during `ConfigurationChangeManager.ReadChanges` (which does network calls to SQS) can block dashboard log-level requests.

## Anti-Patterns

### Redundant Init on Client Creation

**What happens:** `QueueClientFactory.CreateClient` calls `client.Init()` once, and then `ConfigurationChangeManager.FindConfigsToAdd` calls `queueClient.Init()` again immediately after (`Projects/AWSRedrive/Factories/QueueClientFactory.cs`, `Projects/AWSRedrive/ConfigurationChangeManager.cs:~106-108`).
**Why it's wrong:** `Init()` recreates the underlying `AmazonSQSClient`, discarding the first one — wasted allocation and a leftover code comment (`// ← Add this line`) suggesting this was an unintentional duplication.
**Do this instead:** Call `Init()` in exactly one place (either inside the factory or inside the change manager, not both).

### Synchronous Blocking on Async SDK Calls

**What happens:** `AwsQueueClient.GetMessage()`/`GetDlqUrl()`, `HttpMessageProcessor.SendRequest`, and `KafkaMessageProcessor.ProcessMessage` all call `.Result` on `Task`s instead of using `async`/`await` (`Projects/AWSRedrive/AwsQueueClient.cs:56-58,127-135`, `Projects/AWSRedrive/HttpMessageProcessor.cs`, `Projects/AWSRedrive/KafkaMessageProcessor.cs`).
**Why it's wrong:** Ties up a thread-pool thread for the full duration of network I/O (up to 20s for SQS long-poll); with many queues this multiplies thread usage and risks starvation, and any exception is wrapped in `AggregateException`.
**Do this instead:** Make `IQueueClient`/`IMessageProcessor` methods properly `async Task<T>` end-to-end, and make `QueueProcessor.ProcessMessageLoop` an `async Task` loop.

## Error Handling

**Strategy:** Catch-log-continue at the processor level; exceptions in the message loop are caught per-message so one bad message doesn't kill the polling task. Configuration read errors are caught at the `Orchestrator` reconciliation level. HTTP/Kafka/PowerShell processors surface exceptions upward from their `ProcessMessage` call, which `QueueProcessor` catches and records as `MessagesFailed`/`LastError` metrics without deleting the SQS message (allowing redelivery/DLQ).

**Patterns:**
- `QueueProcessor.ProcessMessageLoop` wraps queue receive, processing, and delete in separate `try/catch` blocks so a delete failure doesn't count as a processing failure (`Projects/AWSRedrive/QueueProcessor.cs:129-225`)
- `LogError` helper builds a structured NLog `LogEventInfo` with alias/queue/error properties for both queue-receive and processing failures
- Validation errors during config load throw `FluentValidation.ValidationException`, which propagates up through `Orchestrator.StartProcessing`'s outer `try/catch` and is logged, without crashing the reconciliation loop
- `Worker.ExecuteAsync` (Linux service) catches `OperationCanceledException` as normal shutdown and logs any other exception as `Fatal`

## Cross-Cutting Concerns

**Logging:** NLog throughout, configured via `Projects/AWSRedrive/NLog.config` and routed through the Generic Host in the Linux service (`UseNLog()`) or manually initialized in the console app. `EntryLogger` provides a per-alias/per-message logging façade with an independently adjustable level (separate from NLog's own global rules), used by all queue-processor-owned code paths. `LogManager.GetCurrentClassLogger()` / `LogManager.GetLogger(name)` are used directly in infrastructure classes (`Orchestrator`, `ConfigurationReader`, `ConfigurationChangeManager`).

**Validation:** FluentValidation (`ConfigurationEntryValidator`) enforces structural rules on each `ConfigurationEntry` at read time (`Projects/AWSRedrive/ConfigurationReader.cs`); invalid `config.json` entries cause a `ValidationException` that aborts the reconciliation cycle (previously-running processors are unaffected until the next successful read).

**Authentication:** Handled per-redrive-target inside `HttpMessageProcessor` (API key header, Authorization header, Basic auth via `HttpBasicAuthenticator`) and per-queue inside `AwsQueueClient` (explicit AWS access/secret key, named AWS profile, or default credential chain). No authentication exists on the `DashboardServer` HTTP endpoints themselves.

---

*Architecture analysis: 2026-08-28*
