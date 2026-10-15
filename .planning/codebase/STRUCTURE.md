# Codebase Structure

**Analysis Date:** 2026-08-28

## Directory Layout

```
awsredrive.core/
├── Projects/                       # All buildable application/library source
│   ├── AWSRedrive/                 # Core class library (domain logic, shared by both hosts)
│   │   ├── DI/                     # Composition root (Injector)
│   │   ├── Factories/              # Factories for clients/processors
│   │   ├── Interfaces/             # All public interfaces (I*.cs)
│   │   ├── Models/                 # POCOs: config entries, settings, metrics, DTOs
│   │   ├── Validations/            # FluentValidation validators
│   │   ├── *.cs                    # Orchestrator, QueueProcessor, message processors,
│   │   │                           #   AwsQueueClient, DashboardServer, EntryLogger, etc.
│   │   ├── NLog.config              # NLog logging configuration (copied, not overwritten on publish)
│   │   ├── dashboard.html           # Static dashboard UI served by DashboardServer
│   │   └── AWSRedrive.csproj
│   ├── AWSRedrive.console/          # Executable: plain console host
│   │   ├── Program.cs
│   │   ├── Properties/              # launchSettings.json etc.
│   │   └── AWSRedrive.console.csproj
│   └── AWSRedrive.LinuxService/     # Executable: systemd/Generic Host service
│       ├── Program.cs
│       ├── Worker.cs                # BackgroundService wrapping Orchestrator + DashboardServer
│       ├── Properties/
│       └── AWSRedrive.LinuxService.csproj
├── Tests/
│   ├── AWSRedrive.Tests.Unit/        # xUnit/NUnit-style unit tests, one file per class under test
│   │   ├── Helpers/                  # Test doubles (e.g. SimpleConfigurationReader)
│   │   └── AWSRedrive.Tests.Unit.csproj
│   └── AWSRedrive.Test.Integration/  # Docker-backed integration suite (28 tests)
│       ├── Infrastructure/           # Sink/queue clients, settings, preflight fixture
│       ├── integrationsettings.json  # Endpoints; env-var overridable
│       └── AWSRedrive.Test.Integration.csproj
├── integration-infrastructure/       # The container stack the integration suite runs against
│   ├── docker-compose.yml            # floci, kafka, sink, redrive, dozzle + 2 init containers
│   ├── start.ps1 / stop.ps1          # CLI entry points; lifecycle is manual, not test-driven
│   ├── .env                          # Ports and image tags
│   ├── floci/ kafka/                 # Queue and topic seed scripts
│   ├── redrive/                      # config.json, appsettings.json, NLog.config for the SUT
│   └── sink/                         # Recording sink service (Python/FastAPI) + Dockerfile
├── Solutions/
│   └── AWSRedrive.Core.sln           # Visual Studio solution referencing all five projects
├── docs/superpowers/                 # Design specs and implementation plans
├── appsettings.json                  # App-wide settings: Dashboard, Metrics, DefaultLogLevel
├── config.json                       # Redrive configuration entries (queue → destination mappings)
├── global.json                       # Opts `dotnet test` into Microsoft.Testing.Platform mode (required for xUnit v3)
├── Dockerfile                        # Multi-stage build producing self-contained console/service images
├── Dockerfile.image                  # Alternate/base image Dockerfile
├── Makefile                          # Build/test/publish helper targets
├── README.md                         # Product description and configuration reference
└── schematic.png                     # Architecture diagram referenced from README
```

## Directory Purposes

**`Projects/AWSRedrive/` (core library):**
- Purpose: All domain logic — configuration, orchestration, queue processing, message delivery strategies, logging, metrics, dashboard
- Contains: Concrete implementation classes at the project root; `Interfaces/`, `Models/`, `Factories/`, `Validations/`, `DI/` subfolders for supporting types
- Key files: `Projects/AWSRedrive/Orchestrator.cs`, `Projects/AWSRedrive/QueueProcessor.cs`, `Projects/AWSRedrive/DI/Injector.cs`, `Projects/AWSRedrive/DashboardServer.cs`

**`Projects/AWSRedrive/Interfaces/`:**
- Purpose: All abstractions used for dependency injection and testability
- Contains: One interface per file, named `I<Concept>.cs`
- Key files: `IOrchestrator.cs`, `IQueueProcessor.cs`, `IQueueClient.cs`, `IMessageProcessor.cs`, `IConfigurationReader.cs`, `IConfigurationChangeManager.cs`

**`Projects/AWSRedrive/Models/`:**
- Purpose: Plain data objects — configuration schema, app settings, runtime metrics, wire-format DTOs
- Contains: `ConfigurationEntry.cs` (the config.json entry schema), `AppSettings.cs` (Dashboard/Metrics settings), `QueueMetrics.cs`, `SnsEnvelope.cs` + `MessageAttribute.cs` (SNS envelope parsing)
- Key files: `Models/ConfigurationEntry.cs`, `Models/AppSettings.cs`

**`Projects/AWSRedrive/Factories/`:**
- Purpose: Encapsulate `new`-ing up concrete implementations of interfaces so `Orchestrator`/`ConfigurationChangeManager` stay decoupled from concrete types
- Contains: `MessageProcessorFactory.cs` (selects Http/Kafka/PowerShell processor by config), `QueueClientFactory.cs` (creates `AwsQueueClient`), `QueueProcessorFactory.cs` (creates `QueueProcessor`)

**`Projects/AWSRedrive/Validations/`:**
- Purpose: FluentValidation rule sets for configuration correctness
- Contains: `ConfigurationEntryValidator.cs`

**`Projects/AWSRedrive/DI/`:**
- Purpose: Single composition root for the whole application
- Contains: `Injector.cs` — static class with overloaded `Inject(...)` methods for both self-hosted and externally-supplied `IServiceCollection`

**`Projects/AWSRedrive.console/`:**
- Purpose: Minimal console executable host, intended for interactive/manual runs or Windows use
- Contains: `Program.cs` only (plus `Properties/launchSettings.json`)

**`Projects/AWSRedrive.LinuxService/`:**
- Purpose: Production service host using the .NET Generic Host, intended for systemd/Linux deployment
- Contains: `Program.cs` (host bootstrap) and `Worker.cs` (`BackgroundService` implementation)

**`Tests/AWSRedrive.Tests.Unit/`:**
- Purpose: Fast, isolated unit tests, one file per class under test, named `<ClassUnderTest>Tests.cs`
- Contains: Test classes plus a `Helpers/` folder for test doubles (e.g. `SimpleConfigurationReader.cs`)

**`Tests/AWSRedrive.Test.Integration/`:**
- Purpose: End-to-end tests against a running container stack - SQS in, HTTP and Kafka out
- Contains: eight `*Tests.cs` files grouped by behaviour (verbs, auth, headers, retry, TLS, Kafka, config reload, dashboard) plus an `Infrastructure/` folder of clients and fixtures
- Requires the stack to be running first (`integration-infrastructure/start.ps1`); it holds no Docker dependency of its own

**`integration-infrastructure/`:**
- Purpose: Every container the integration suite needs, plus the scripts that start and stop them
- Contains: `docker-compose.yml`, `start.ps1`/`stop.ps1`, `.env`, per-service subfolders, and its own `README.md`
- Lifecycle is deliberately manual and separate from `dotnet test` - the stack is started from a terminal, then tests run from Visual Studio or the CLI

**`Solutions/`:**
- Purpose: Holds the single `.sln` file referencing all five projects (core library, two hosts, two test projects)

## Key File Locations

**Entry Points:**
- `Projects/AWSRedrive.console/Program.cs`: Console host `Main`
- `Projects/AWSRedrive.LinuxService/Program.cs`: Generic Host `Main`
- `Projects/AWSRedrive.LinuxService/Worker.cs`: `BackgroundService.ExecuteAsync`

**Configuration:**
- `appsettings.json` (repo root): Dashboard/Metrics/DefaultLogLevel — copied next to each executable
- `config.json` (repo root): Redrive queue-to-destination mappings — copied next to each executable, hot-reloaded every 60s
- `Projects/AWSRedrive/NLog.config`: NLog targets/rules (copy-to-output = Never, so deployments must supply their own)
- `Projects/AWSRedrive/Models/AppSettings.cs`: Schema for `appsettings.json`
- `Projects/AWSRedrive/Models/ConfigurationEntry.cs`: Schema for each `config.json` entry

**Core Logic:**
- `Projects/AWSRedrive/Orchestrator.cs`: Top-level supervisor loop
- `Projects/AWSRedrive/ConfigurationChangeManager.cs`: Config diff/reconciliation logic
- `Projects/AWSRedrive/QueueProcessor.cs`: Per-queue message pump
- `Projects/AWSRedrive/AwsQueueClient.cs`: SQS transport implementation
- `Projects/AWSRedrive/HttpMessageProcessor.cs`, `Projects/AWSRedrive/KafkaMessageProcessor.cs`, `Projects/AWSRedrive/PowershellMessageProcessor.cs`: Redrive delivery strategies
- `Projects/AWSRedrive/DashboardServer.cs`: Embedded status/monitoring web API

**Testing:**
- `Tests/AWSRedrive.Tests.Unit/`: One `*Tests.cs` file per production class (e.g. `OrchestratorTests.cs`, `QueueProcessorFactoryTests.cs`, `HttpMessageProcessorTests.cs`)
- `Tests/AWSRedrive.Tests.Unit/Helpers/SimpleConfigurationReader.cs`: Test double for `IConfigurationReader`
- `Tests/AWSRedrive.Test.Integration/Infrastructure/SinkClient.cs`: Polls the sink for deliveries by correlation id
- `Tests/AWSRedrive.Test.Integration/Infrastructure/StackPreflight.cs`: Assembly fixture; fails with instructions when the stack is down
- `integration-infrastructure/redrive/config.json`: The 17 aliases the integration suite exercises
- `integration-infrastructure/sink/app.py`: Recording sink - HTTP + Kafka recorder, auth challenges, config admin
- `integration-infrastructure/README.md`: How the stack fits together and how tests stay isolated

## Naming Conventions

**Files:**
- One public type per file, file name matches the type name exactly (e.g. `Orchestrator.cs` contains `class Orchestrator`)
- Interfaces are prefixed `I` and live in `Interfaces/` (e.g. `IQueueProcessor.cs`)
- Test files are named `<ClassUnderTest>Tests.cs` and live in the flat `Tests/AWSRedrive.Tests.Unit/` directory (no subfolder mirroring of source namespaces, except `Helpers/`)
- Factories are named `<Product>Factory.cs` and live in `Factories/`
- Validators are named `<Type>Validator.cs` and live in `Validations/`

**Directories:**
- Top-level `Projects/` holds all buildable source (library + executables); `Tests/` holds all test projects; `Solutions/` holds only the `.sln`
- Within `Projects/AWSRedrive/`, subfolders group by role (`Interfaces/`, `Models/`, `Factories/`, `Validations/`, `DI/`), while most concrete implementation classes sit flat at the project root rather than being grouped by feature

## Where to Add New Code

**New redrive delivery target (e.g. a new protocol):**
- Implementation: New class in `Projects/AWSRedrive/` implementing `IMessageProcessor` (see `Projects/AWSRedrive/HttpMessageProcessor.cs` as a template)
- Wire-up: Add a new mutually-exclusive config field to `Projects/AWSRedrive/Models/ConfigurationEntry.cs`, extend the selection logic in `Projects/AWSRedrive/Factories/MessageProcessorFactory.cs`, and add a validation rule in `Projects/AWSRedrive/Validations/ConfigurationEntryValidator.cs`
- Tests: New `<Name>Tests.cs` in `Tests/AWSRedrive.Tests.Unit/`

**New queue transport (e.g. non-SQS):**
- Implementation: New class implementing `IQueueClient` (see `Projects/AWSRedrive/AwsQueueClient.cs`), new corresponding `IQueueClientFactory` implementation in `Projects/AWSRedrive/Factories/`
- Wire-up: Register in `Projects/AWSRedrive/DI/Injector.cs`

**New configuration field:**
- Add the property to `Projects/AWSRedrive/Models/ConfigurationEntry.cs`
- Update `Projects/AWSRedrive/ConfigurationChangeManager.cs`'s `ConfigurationsMatch` comparison if the field affects processor identity
- Document it in `README.md`'s configuration entry list

**New dashboard API endpoint:**
- Add a new `_app.Map*` route inside `DashboardServer.Start()` in `Projects/AWSRedrive/DashboardServer.cs`
- Frontend changes go in `Projects/AWSRedrive/dashboard.html`

**Utilities:**
- Shared, cross-cutting helpers (logging, metrics) live directly in `Projects/AWSRedrive/` at the project root (e.g. `EntryLogger.cs`, `MetricsStore.cs`) rather than in a dedicated `Utils/`/`Common/` folder — follow this convention for new small stateless helpers rather than introducing a new subfolder.

## Special Directories

**`Projects/*/bin/`, `Projects/*/obj/`, `Tests/*/bin/`, `Tests/*/obj/`:**
- Purpose: MSBuild output/intermediate artifacts
- Generated: Yes
- Committed: No (excluded via `.gitignore`)

**`Projects/AWSRedrive/dashboard.html`:**
- Purpose: Static single-page dashboard UI, read from disk at runtime and cached in memory (including a gzip-compressed copy) by `DashboardServer`
- Generated: No (hand-authored)
- Committed: Yes; copied to output directory on build (`CopyToOutputDirectory: PreserveNewest`)

**`config.json` (repo root):**
- Purpose: Runtime redrive configuration, re-read every 60 seconds without restart
- Generated: No (hand-authored / environment-specific); root copy in the repo is an empty array (`[]`) placeholder
- Committed: Yes (as a placeholder — real deployments overwrite this file)

---

*Structure analysis: 2026-08-28*
