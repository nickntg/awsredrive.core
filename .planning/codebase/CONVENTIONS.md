# Coding Conventions

**Analysis Date:** 2026-08-28

## Naming Patterns

**Files:**
- One public type per file, file name matches the type name exactly (PascalCase): `Orchestrator.cs` contains `class Orchestrator`, `QueueProcessor.cs` contains `class QueueProcessor`.
- Interfaces prefixed with `I` and live under `Projects/AWSRedrive/Interfaces/`: `IQueueProcessor.cs`, `IConfigurationReader.cs`, `IMessageProcessorFactory.cs`.
- Test files named `<ClassUnderTest>Tests.cs` in `Tests/AWSRedrive.Tests.Unit/`: `OrchestratorTests.cs`, `AwsQueueClientTests.cs`, `ConfigurationEntryValidatorTests.cs`.
- Test helper/fake classes live in `Tests/AWSRedrive.Tests.Unit/Helpers/`: `SimpleConfigurationReader.cs`.

**Functions/Methods:**
- PascalCase for all public and private methods: `ProcessMessage`, `CreateGetRequest`, `CheckLogLevelExpiry`, `TruncateForLog`.
- Verb-first naming that states the action: `ReadChanges`, `SetLogLevel`, `GetOrCreate`.

**Variables:**
- camelCase for locals and parameters: `configurationEntry`, `mockSqsClient`, `lastDateTimeChecked`.
- Private instance fields prefixed with underscore, camelCase: `_configurationReader`, `_task`, `_cancellation`, `_lock`.
- `const` fields use PascalCase: `MaxMessageContentSize`.
- `static readonly` fields use PascalCase: `Logger`, `LogLevelTimeout`.

**Types:**
- Classes and interfaces: PascalCase (`ConfigurationEntry`, `EntryLogger`, `IQueueClient`).
- DTO/model classes are plain data holders with auto-properties only, no logic (`Projects/AWSRedrive/Models/ConfigurationEntry.cs`).

## Code Style

**Formatting:**
- No `.editorconfig`, `.prettierrc`, or dotnet-format config file present in the repo — formatting is not enforced by tooling. Follow existing file conventions manually (4-space indentation, Allman brace style — opening brace on its own line — as seen throughout `Projects/AWSRedrive/*.cs`).
- ReSharper/Rider project-level dictionary settings exist at `Solutions/AWSRedrive.Core.sln.DotSettings` (adds custom words to spellcheck dictionary only — not a style ruleset).
- No linter (ESLint/StyleCop/Roslyn analyzers) configured in `.csproj` files.

**Language features:**
- Target framework: `net8.0` for all projects (`Projects/AWSRedrive/AWSRedrive.csproj`, `Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj`).
- C# collection expressions used where terse: `private readonly string[] _ignoredHeaders = ["content-length", "host", ...];` (`Projects/AWSRedrive/HttpMessageProcessor.cs:16`).
- Nested ternaries used for simple branching logic: `configurationEntry.UseGET ? "GET" : configurationEntry.UsePUT ? "PUT" : ... : "POST"` (`Projects/AWSRedrive/HttpMessageProcessor.cs:21`).
- `var` used pervasively for local variable declarations.
- Expression-bodied members used for simple one-line methods/properties: `private bool IsEnabled(LogLevel level) => level >= _minLevel;` (`Projects/AWSRedrive/EntryLogger.cs:55`).

## Import Organization

**Order:**
1. `System.*` namespaces first
2. Third-party/framework namespaces (`Amazon.*`, `NLog`, `Newtonsoft.Json`, `RestSharp`, `FakeItEasy`, `Xunit`)
3. Project namespaces (`AWSRedrive.Interfaces`, `AWSRedrive.Models`, `AWSRedrive.Validations`)

**Namespace layout:**
- Root namespace `AWSRedrive` for core classes at `Projects/AWSRedrive/`.
- Sub-namespaces match folder names exactly: `AWSRedrive.Interfaces`, `AWSRedrive.Models`, `AWSRedrive.Validations`, `AWSRedrive.Factories`, `AWSRedrive.DI`.
- Test namespaces mirror source: `AWSRedrive.Tests.Unit`, `AWSRedrive.Tests.Unit.Helpers`, `AWSRedrive.Test.Integration`.
- No path aliases (not applicable in C#/.csproj — uses standard namespace/using resolution).

## Error Handling

**Patterns:**
- Long-running loops (`Orchestrator.StartProcessing`, `QueueProcessor.ProcessMessageLoop`) wrap risky work in `try/catch (Exception e)` and continue the loop rather than crashing the processing thread. See `Projects/AWSRedrive/Orchestrator.cs:59-69` and `Projects/AWSRedrive/QueueProcessor.cs:142-158` / `:181-216`.
- Failures inside the message loop are captured per-stage (receive vs. process vs. delete) so one message's failure doesn't abort the loop — each stage has its own `try/catch` (`Projects/AWSRedrive/QueueProcessor.cs`).
- Domain/business failures throw typed exceptions with a descriptive message: `throw new InvalidOperationException($"Received {response.StatusCode} status code with content [{response.Content}]");` (`Projects/AWSRedrive/HttpMessageProcessor.cs:216`).
- When parsing may legitimately fail and failure is not exceptional, a bare `catch { }` is used with a one-line comment explaining why it's safe to ignore: `catch { // Ignored - not an SNS envelope }` (`Projects/AWSRedrive/HttpMessageProcessor.cs:73-76`) and `catch { // If parsing fails, just use the request without query parameters }` (`Projects/AWSRedrive/HttpMessageProcessor.cs:97-100`).
- Validation errors use FluentValidation (`AbstractValidator<T>`) rather than manual exception throwing — see `Projects/AWSRedrive/Validations/ConfigurationEntryValidator.cs`. Validation failures are returned as a `ValidationResult` (`result.IsValid`, `result.Errors`), not thrown.
- Errors that should surface to the caller in constructors/synchronous methods use `Record.Exception(() => ...)` pattern in tests (see Testing section) rather than assuming no throw.
- Metrics are updated alongside error logging so failures are observable without needing to inspect logs (`_metrics.LastError = DateTime.UtcNow; _metrics.LastErrorMessage = e.Message;` — `Projects/AWSRedrive/QueueProcessor.cs`).

## Logging

**Framework:** NLog (`NLog`, `NLog.Web.AspNetCore` packages), wrapped by a custom `EntryLogger` class (`Projects/AWSRedrive/EntryLogger.cs`).

**Patterns:**
- Do not call `NLog.LogManager` directly from business logic — use the `EntryLogger` wrapper, which:
  - binds a per-configuration-entry alias and per-message-scoped `messageId` to every log event (`evt.Properties["alias"]`, `evt.Properties["messageId"]`).
  - supports a runtime-adjustable minimum log level per entry via `SetLogLevel(string level)` (backing a dashboard feature to change verbosity live).
  - derives the logger name automatically from the caller's file via `[CallerFilePath]` (no manual `LogManager.GetLogger(typeof(X))` boilerplate needed at call sites).
- Use `logger.WithMessageId(msg.MessageId)` to get a scoped child logger for all logs related to processing one message (`Projects/AWSRedrive/QueueProcessor.cs:165`).
- Guard expensive log content with the `Is<Level>Enabled` properties before building large strings: `if (logger.IsTraceEnabled && !string.IsNullOrEmpty(response.Content)) { ... }` (`Projects/AWSRedrive/HttpMessageProcessor.cs:192-198`).
- Truncate large payloads before logging using the `TruncateForLog`/`TruncateMessage` helpers (`Projects/AWSRedrive/QueueProcessor.cs:234-254`) — never log full message bodies unguarded.
- Structured/metrics-oriented logs use a dedicated `Logger` (`LogManager.GetLogger("Metrics")`) and build a `LogEventInfo` with explicit `Properties[...]` key/value pairs rather than interpolated strings, so they can be parsed as structured data (`Projects/AWSRedrive/QueueProcessor.cs:268-285`).
- Log levels used consistently: `Trace` (verbose polling/payload detail), `Debug` (lifecycle/step markers), `Info` (state changes worth operator attention), `Warn` (recoverable issues), `Error`/`Fatal` (failures, always paired with the exception object).

## Comments

**When to Comment:**
- Sparse comments overall; used mainly to explain *why* something non-obvious is done, not *what* the code does.
- Empty/`catch { }` blocks always carry a one-line comment justifying the swallow (see Error Handling above).
- Magic numbers get an inline comment: `private const int MaxMessageContentSize = 100 * 1024; // 100KB` (`Projects/AWSRedrive/QueueProcessor.cs:14`).
- Test comments favor `// Arrange` / `// Act` / `// Assert` section markers within a single `[Fact]` method (see Testing).

**XML doc comments:**
- Used selectively on public members whose intent isn't obvious from the name, e.g. `/// <summary>Dead Letter Queue URL - auto-populated from SQS RedrivePolicy on startup</summary>` on `ConfigurationEntry.DlqUrl` (`Projects/AWSRedrive/Models/ConfigurationEntry.cs:31-34`), and `/// <summary>Creates a new logger with message context for correlation</summary>` on `EntryLogger.WithMessageId` (`Projects/AWSRedrive/EntryLogger.cs:28-30`).
- Not applied uniformly — most methods and classes have no XML doc comments.

## Function Design

**Size:** Methods generally stay small and single-purpose (a few lines to ~20 lines); larger orchestration methods (e.g. `QueueProcessor.ProcessMessageLoop`) are decomposed into private helpers (`CheckLogLevelExpiry`, `CheckPeriodicMetrics`, `LogMetrics`, `LogError`, `TruncateMessage`).

**Parameters:**
- Dependencies passed explicitly through constructors (constructor injection) rather than service-locator lookups — e.g. `Orchestrator` takes five interface dependencies in its constructor (`Projects/AWSRedrive/Orchestrator.cs:28-32`).
- Optional/tunable parameters use C# default parameter values: `public QueueProcessor(IMetricsSettings metricsSettings, string defaultLogLevel = "Error")` (`Projects/AWSRedrive/QueueProcessor.cs:36`).
- Methods that need call-site file context use `[CallerFilePath]` attribute defaults rather than requiring the caller to pass it explicitly (`Projects/AWSRedrive/EntryLogger.cs:80` etc.).

**Return Values:**
- Nullable returns (`string GetLogLevel(string alias)`, `IMessage GetMessage()`) are used to signal "not found"/"no data" instead of throwing, when absence is a normal/expected outcome. Callers are expected to null-check.
- Boolean returns (`bool SetLogLevel(...)`) signal success/failure of an operation that has an obvious failure mode (unknown alias), rather than throwing.

## Module Design

**Exports/Visibility:**
- Concrete implementation classes are `public` and implement a corresponding `public interface` (`Orchestrator : IOrchestrator`, `QueueProcessor : IQueueProcessor`, `HttpMessageProcessor : IMessageProcessor`) — enables faking/mocking in unit tests via FakeItEasy.
- Internal helper methods that don't need to be part of the public contract are `private` (e.g. `SendRequest` in `HttpMessageProcessor`), but many implementation methods are left `public` specifically so unit tests can exercise them directly without mocking internals (e.g. `CreateGetRequest`, `CreateOptions`, `AddAuthentication` on `HttpMessageProcessor` — see `Tests/AWSRedrive.Tests.Unit/HttpMessageProcessorTests.cs`).

**Dependency injection:**
- Factories (`Projects/AWSRedrive/Factories/MessageProcessorFactory.cs`, `QueueClientFactory.cs`, `QueueProcessorFactory.cs`) encapsulate object creation behind `I*Factory` interfaces so orchestration code depends on abstractions, not `new`.
- `Projects/AWSRedrive/DI/Injector.cs` centralizes `Microsoft.Extensions.DependencyInjection` service registration for the composition root (console/service entry points).

**Barrel Files:** Not used (not a common C# pattern in this codebase) — each type is referenced by its own namespace/using statement.

---

*Convention analysis: 2026-08-28*
