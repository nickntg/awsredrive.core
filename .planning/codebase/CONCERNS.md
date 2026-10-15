# Codebase Concerns

**Analysis Date:** 2026-08-28

## Tech Debt

**Redundant SQS client initialization:**
- Issue: `QueueClientFactory.CreateClient()` already calls `client.Init()` internally (see the leftover dev comment `// ← Add this line` still in the file), but `ConfigurationChangeManager.FindConfigsToAdd()` calls `queueClient.Init()` again immediately after `queueClientFactory.CreateClient(config)`.
- Files: `Projects/AWSRedrive/Factories/QueueClientFactory.cs:8-13`, `Projects/AWSRedrive/ConfigurationChangeManager.cs:113-114`
- Impact: Creates two `AmazonSQSClient` instances per queue processor startup (the first is discarded/garbage collected); wastes a connection setup and is confusing to read — looks like a bug waiting to be "fixed" by someone unaware it's already redundant.
- Fix approach: Remove the internal `client.Init()` call from `QueueClientFactory.CreateClient()` (make factory responsible only for construction) or remove the explicit `queueClient.Init()` call in `ConfigurationChangeManager`. Pick one owner of initialization.

**Test file / class naming mismatch:**
- Issue: `Tests/AWSRedrive.Tests.Unit/QueueManagerTests.cs` contains a class `QueueManagerTests` that actually exercises `QueueProcessor` (there is no `QueueManager` class in the codebase).
- Files: `Tests/AWSRedrive.Tests.Unit/QueueManagerTests.cs`, `Projects/AWSRedrive/QueueProcessor.cs`
- Impact: Confuses navigation from test name to production class; likely a leftover from an earlier rename of `QueueManager` → `QueueProcessor` that wasn't fully propagated to tests.
- Fix approach: Rename file/class to `QueueProcessorTests` (a `QueueProcessorTests.cs` doesn't currently exist as a separate file, so this is a straightforward rename).

**Blocking async-over-sync (`.Result`) on hot paths:**
- Issue: Every message receive, delete-completion wait indirectly, DLQ lookup, HTTP redrive, and Kafka produce blocks a thread synchronously via `.Result` instead of awaiting.
- Files: `Projects/AWSRedrive/AwsQueueClient.cs:57` (`GetMessage`), `Projects/AWSRedrive/AwsQueueClient.cs:132` (`GetDlqUrl`), `Projects/AWSRedrive/HttpMessageProcessor.cs:184` (`SendRequest`), `Projects/AWSRedrive/KafkaMessageProcessor.cs:47` (`ProduceAsync(...).Result`)
- Impact: Each `QueueProcessor` runs its message loop on a dedicated `LongRunning` `Task` (one OS thread per configured queue), so this doesn't deadlock today, but it means N configured queues = N dedicated blocked OS threads, and any future refactor toward a shared thread/async pump will hit this immediately. It also means SQS long-polling (20s) and HTTP/Kafka timeouts fully occupy a thread each with no concurrency within a single queue.
- Fix approach: Convert `ProcessMessageLoop`, `IQueueClient`, and `IMessageProcessor` to async all the way through (`async Task`) and run via `Task.Run`/async loop instead of blocking `.Result` calls.

**`async void` message deletion swallows/misroutes exceptions:**
- Issue: `AwsQueueClient.DeleteMessage()` calls `DeleteMessageInternalAsync(message)`, which is declared `async void`.
- Files: `Projects/AWSRedrive/AwsQueueClient.cs:105-119`
- Impact: `QueueProcessor.ProcessMessageLoop` wraps the call to `_queueClient.DeleteMessage(msg)` in a try/catch expecting to log delete failures (`Projects/AWSRedrive/QueueProcessor.cs` around the `Deleting from queue` block), but because the method is `async void`, the try/catch only covers the synchronous portion before the first `await`. Any exception thrown after the `await _client.DeleteMessageAsync(...)` call (e.g., invalid receipt handle, throttling, network fault) is not caught by that try/catch — it surfaces on whatever context resumes the continuation and, being unhandled in an `async void` method, can crash the entire process (unhandled exceptions in `async void` are rethrown on the `SynchronizationContext`/thread pool and are fatal by default in .NET).
- Fix approach: Change `DeleteMessage` to return `Task` (`async Task DeleteMessageInternalAsync(...)`) and have `IQueueClient.DeleteMessage` awaited (or synchronously waited with try/catch) by the caller so failures are actually caught and logged instead of potentially crashing the process.

**Global static `MetricsStore` never garbage-collects removed queues:**
- Issue: `MetricsStore` is a `static` `ConcurrentDictionary<string, QueueMetrics>` keyed by alias; `ConfigurationChangeManager` never calls `MetricsStore.Remove(alias)` when a processor is stopped/removed.
- Files: `Projects/AWSRedrive/MetricsStore.cs`, `Projects/AWSRedrive/ConfigurationChangeManager.cs` (`FindEntriesToRemove` / removal loop)
- Impact: Long-running instances that frequently add/remove/rename queue configs accumulate stale `QueueMetrics` entries forever (unbounded, if aliases churn) and stale entries continue to appear in `/api/status` for aliases no longer in `config.json` unless the config still lists them (dashboard reads `allConfigs` from the file, so a removed config entry disappears from output, but the metrics object itself is never freed from the static dictionary).
- Fix approach: Call `MetricsStore.Remove(processor.Configuration.Alias)` in the `toRemove` loop in `ConfigurationChangeManager.ReadChanges`.

**Global mutable static state (`MetricsStore`, `Injector.Container`):**
- Files: `Projects/AWSRedrive/MetricsStore.cs`, `Projects/AWSRedrive/DI/Injector.cs`
- Impact: Static singletons make parallel/unit testing more fragile (shared state across test runs in the same process) and prevent running multiple independent `Orchestrator` instances in one process (e.g., for future multi-tenancy).

## Known Bugs

**Alias defaulting only covers HTTP redrive:**
- Symptoms: `ConfigurationReader.ReadConfiguration()` only defaults `entry.Alias = entry.RedriveUrl` when `Alias` is empty. If a config entry uses `RedriveScript` or `RedriveKafkaTopic` instead of `RedriveUrl` and omits `Alias`, the alias stays `null`.
- Files: `Projects/AWSRedrive/ConfigurationReader.cs:26-29`
- Trigger: Add a `config.json` entry with `RedriveScript` (or `RedriveKafkaTopic`) set and no `Alias`.
- Workaround: Always set `Alias` explicitly for script/Kafka redrive entries. A `null` alias will match `MetricsStore.GetOrCreate(null)`, causing multiple such processors to share one metrics bucket, and `Orchestrator.SetLogLevel`/`GetLogLevel` (which look up by alias) become ambiguous across all null-alias processors.

**Dashboard log-level API changes persist past config reload only for `LogLevel`, but a full config change resets it silently:**
- Symptoms: `ConfigurationChangeManager.ConfigurationsMatch` intentionally excludes `LogLevel` so that a runtime-set log level (via `/api/loglevel/{alias}`) survives a `config.json` re-read that doesn't otherwise change anything. However, if *any other* field in the config entry changes (even unrelated to log level, e.g., `Timeout`), the whole processor is torn down and recreated with the *file's* `LogLevel`/`_defaultLogLevel`, silently discarding the runtime override with no log message distinguishing "recreated due to unrelated change" from "recreated due to log level change."
- Files: `Projects/AWSRedrive/ConfigurationChangeManager.cs:52-82`, `Projects/AWSRedrive/QueueProcessor.cs:59-63` (`SetLogLevel`)
- Trigger: Set a runtime log level via the dashboard, then edit any other field of that same config entry in `config.json` (e.g. `Timeout`).
- Workaround: None currently; operators should expect log-level overrides to be fragile across any config edit.

## Security Considerations

**Dashboard API has no authentication and binds to all interfaces:**
- Risk: `DashboardServer.Start()` calls `options.ListenAnyIP(_settings.Port)` and exposes `/api/status`, `/api/stream` (SSE), and — critically — `POST /api/loglevel/{alias}` with zero authentication/authorization. `appsettings.json` in the repo has `Dashboard.Enabled: true` by default.
- Files: `Projects/AWSRedrive/DashboardServer.cs:75-186`, `appsettings.json`
- Current mitigation: None in code. Presumably relies on network-level controls (firewall/security group) that are outside this repo.
- Recommendations: Add at minimum a shared-secret/API-key check or bind to `127.0.0.1` by default, document the requirement to firewall the dashboard port, and consider making log-level changes require an explicit opt-in/auth token before shipping this to environments without external network controls.

**Secrets stored in plaintext in `config.json`, which is tracked by git:**
- Risk: `ConfigurationEntry` includes `AccessKey`, `SecretKey`, `AuthToken`, `BasicAuthPassword`, `AwsGatewayToken` fields all read directly from `config.json` in plaintext (`Projects/AWSRedrive/ConfigurationReader.cs`). `config.json` is not listed in `.gitignore` (only `bin/`, `obj/`, `.user`, `.suo`, `.vs/`, `packages/`, `Debug/`, `.sonarqube/`, `sonar*.*`, `publish/` are excluded), and it is currently committed to the repo (as `[]`).
- Files: `config.json`, `.gitignore`, `Projects/AWSRedrive/Models/ConfigurationEntry.cs`
- Current mitigation: The tracked copy currently contains an empty array, so no live secret is leaked today, but the file is one `git add -A && git commit` away from committing real credentials since there is no gitignore guard or secret-scanning check.
- Recommendations: Add `config.json` to `.gitignore` (keep only a `config.example.json` template in git), and/or support pulling secrets from environment variables or a secrets manager instead of plaintext JSON fields.

**`IgnoreCertificateErrors` disables TLS certificate validation entirely:**
- Risk: `HttpMessageProcessor.CreateOptions` sets `options.RemoteCertificateValidationCallback = (_, _, _, _) => true;` when `configurationEntry.IgnoreCertificateErrors` is true, accepting any certificate (invalid, expired, self-signed, or from a completely different host) for that redrive target.
- Files: `Projects/AWSRedrive/HttpMessageProcessor.cs:118-121`
- Current mitigation: Opt-in per config entry (`IgnoreCertificateErrors: true` must be explicitly set).
- Recommendations: Keep this as an explicit opt-in but add a startup warning log when any active config entry has it enabled, so misconfigurations show up in logs during operations review.

**Dashboard `/api/status` exposes internal configuration surface:**
- Risk: The status endpoint returns `QueueUrl`, `RedriveUrl`/`RedriveScript`/`RedriveKafkaTopic`, `KafkaBootstrapServers`, and boolean "has secret" flags (`HasAccessKey`, `HasAuthToken`, etc.) for every configured queue to any caller who can reach the port (no auth, see above).
- Files: `Projects/AWSRedrive/DashboardServer.cs:216-262`
- Current mitigation: Actual secret values are not included (only booleans indicating presence), which is good practice, but the URLs/topics/queue names themselves (internal infrastructure topology) are exposed to any network-reachable caller.
- Recommendations: Pair with the authentication fix above; until then, treat the dashboard port as sensitive and restrict network access.

## Performance Bottlenecks

**One dedicated OS thread per configured queue, fully blocked on synchronous calls:**
- Problem: `QueueProcessor.Start()` creates a `TaskCreationOptions.LongRunning` task per queue that runs a `while` loop calling blocking `.Result` operations (SQS long-poll receive, HTTP send, Kafka produce, or PowerShell script execution) with no async yielding.
- Files: `Projects/AWSRedrive/QueueProcessor.cs:83-95` (`Start`), `Projects/AWSRedrive/AwsQueueClient.cs`, `Projects/AWSRedrive/HttpMessageProcessor.cs`, `Projects/AWSRedrive/KafkaMessageProcessor.cs`
- Cause: Synchronous `.Result` blocking calls throughout the pipeline (see Tech Debt above) combined with one thread per queue rather than a shared async event loop.
- Improvement path: Move to `async Task`-based processing so many queues can be served by a small pool of threads via the thread pool's async I/O completion, improving scalability when the number of configured queues grows into the dozens/hundreds.

**PowerShell script re-read and re-parsed from disk on every message:**
- Problem: `PowerShellMessageProcessor.ProcessMessage` calls `File.ReadAllText(scriptPath)` and creates a fresh `PowerShell.Create()` runspace for every single message processed through a `RedriveScript` target.
- Files: `Projects/AWSRedrive/PowershellMessageProcessor.cs:32-38`
- Cause: No caching of script content or reuse of a runspace/pool across invocations.
- Improvement path: Cache script text (invalidate on file mtime change) and consider a `RunspacePool` to avoid the overhead of creating a new PowerShell host per message, especially for high-throughput queues.

## Fragile Areas

**`ConfigurationsMatch` full-field equality drives processor teardown/recreation:**
- Files: `Projects/AWSRedrive/ConfigurationChangeManager.cs:56-82`
- Why fragile: Any single field change in `config.json` for an entry (aside from `LogLevel`) causes the entire `IQueueProcessor` (and its underlying `IQueueClient`/SQS connection) to be torn down and recreated on the next 60-second poll, even for changes that don't require reconnecting (e.g., `Timeout`). This also means in-flight messages are abandoned mid-processing when `Stop()` is called (see `QueueProcessor.Stop()`, 30s wait then continues regardless), since there is no drain/checkpoint mechanism — an in-flight message being processed (e.g., an HTTP call in progress) at the moment of `Stop()` may be interrupted, potentially causing the same SQS message to be redelivered and reprocessed after visibility timeout.
- Safe modification: When adding new `ConfigurationEntry` fields that don't affect connection/auth (e.g., cosmetic or logging-only fields), explicitly exclude them from `ConfigurationsMatch` similarly to how `LogLevel` is excluded, to avoid unnecessary processor churn.
- Test coverage: `Tests/AWSRedrive.Tests.Unit/ConfigurationChangeManagerTests.cs` covers matching logic but does not appear to test the in-flight message interruption scenario.

**`Orchestrator.StartProcessing` polls `config.json` every 60 seconds inside a `while` + `Thread.Sleep(1000)` loop:**
- Files: `Projects/AWSRedrive/Orchestrator.cs:52-79`
- Why fragile: Uses busy-polling with `Thread.Sleep` (not a `Timer` or async delay) on a dedicated `Task`; a `ValidationException` thrown from `ConfigurationReader.ReadConfiguration()` (e.g., malformed `config.json`) is caught and logged each cycle, but the orchestrator keeps retrying every 60s indefinitely with no backoff, alerting, or circuit breaker if `config.json` is persistently invalid — operators may not notice new/changed queues are silently not being picked up.
- Safe modification: Any change to the reconciliation loop should preserve the `lock (_lock)` around `_processors` mutations, since `SetLogLevel`, `GetLogLevel`, and `GetConfigurations` on `Orchestrator` are called concurrently (e.g., from dashboard HTTP requests) while this loop mutates `_processors`.
- Test coverage: `Tests/AWSRedrive.Tests.Unit/OrchestratorTests.cs` exists but a targeted test for "malformed config.json is retried without crashing/alerting" is not evident from file listing.

**`DashboardServer.GetStatus()` merges two config sources by alias, with silent precedence rules:**
- Files: `Projects/AWSRedrive/DashboardServer.cs:216-262`
- Why fragile: Merges `_configReader.ReadConfiguration()` (raw file, all entries including inactive) with `_orchestrator.GetConfigurations()` (only active, running processors, which carry the auto-populated `DlqUrl`) by `Alias` key. If two config entries in `config.json` share the same `Alias` (nothing in `ConfigurationEntryValidator` prevents duplicate aliases), `runtimeByAlias.ToDictionary` will throw `ArgumentException: An item with the same key has already been added` when building the dictionary, taking down the entire `/api/status` and `/api/stream` endpoints.
- Safe modification: Add alias-uniqueness validation to `ConfigurationEntryValidator` (`Projects/AWSRedrive/Validations/ConfigurationEntryValidator.cs`) to prevent this at config-load time rather than failing at dashboard-render time.
- Test coverage: `Tests/AWSRedrive.Tests.Unit/ConfigurationEntryValidatorTests.cs` does not appear to test duplicate-alias rejection (no such rule exists in the validator).

## Scaling Limits

**One SQS long-poll connection + one dedicated thread per configured queue:**
- Current capacity: Not benchmarked in-repo; architecture assumes a small-to-moderate number of configured queues (each gets its own `LongRunning` task, `AmazonSQSClient`, and potentially its own `PowerShell`/`RestClient`/Kafka `Producer` instances created per message).
- Limit: OS thread and connection-count limits become relevant as the number of active `config.json` entries grows into the hundreds; each queue also independently re-creates PowerShell runspaces or Kafka producers per message (see Performance Bottlenecks), multiplying overhead linearly with both queue count and message volume.
- Scaling path: Move to async I/O (shared thread pool) as described above, and pool/reuse expensive per-message resources (PowerShell runspace, Kafka producer) per queue instead of per message.

## Dependencies at Risk

**`System.Management.Automation` (PowerShell SDK) dependency for `RedriveScript`:**
- Risk: Embedding the PowerShell hosting API ties the console/service binaries to a specific PowerShell SDK version and platform behavior; cross-platform (Linux) PowerShell scripting support can have subtle differences from Windows PowerShell, and this is one of only three redrive mechanisms (HTTP, PowerShell, Kafka) with no fallback if the hosting API has compatibility issues on newer .NET/OS combinations.
- Impact: Any breaking change in the PowerShell SDK across .NET version bumps could silently break all `RedriveScript`-configured queues at runtime rather than compile time, since script execution failures manifest as `InvalidOperationException` at message-processing time.
- Now concrete, not hypothetical: the .NET 8 -> 10 upgrade moved the SDK from 7.4.6 to 7.6.5 (the 7.6.x line is the one built for .NET 10). The self-contained single-file publish and the container build both succeed, and the full integration suite passes - but **none of that exercises PowerShell**. All 17 aliases in `integration-infrastructure/redrive/config.json` are HTTP or Kafka destinations; not one uses `RedriveScript`. A PowerShell runtime regression would leave every test green.
- Migration plan: None currently documented. The cheapest real coverage would be a `RedriveScript` alias in the integration config pointing at a trivial `.ps1`, which would turn this from an untested assumption into a gate. Until then, validate script destinations by hand whenever the target framework or PowerShell SDK version moves.

## Missing Critical Features

**No dead-letter/failure escalation beyond logging:**
- Problem: When `processor.ProcessMessage` throws (HTTP failure, script error, Kafka produce failure), `QueueProcessor.ProcessMessageLoop` increments `_metrics.MessagesFailed`, logs the error, and the message is neither deleted from SQS nor explicitly retried/re-queued by this code — it simply becomes visible again after SQS's visibility timeout and will be retried indefinitely until it exceeds the queue's own `RedrivePolicy.maxReceiveCount` (handled entirely by SQS/DLQ configuration, not by this application).
- Blocks: There's no application-level backoff, poison-message quarantine, or alerting hook when a specific message fails repeatedly (aside from the generic `LastError`/`LastErrorMessage` metrics fields, which only track the most recent error, not failure counts per message).

## Test Coverage Gaps

**PowerShell and Kafka message processors lack dedicated unit tests:**
- What's not tested: `Projects/AWSRedrive/PowershellMessageProcessor.cs` and `Projects/AWSRedrive/KafkaMessageProcessor.cs` have no `PowerShellMessageProcessorTests.cs` / `KafkaMessageProcessorTests.cs`. They are only indirectly referenced via `Tests/AWSRedrive.Tests.Unit/MessageProcessorFactoryTests.cs`, which tests factory selection logic, not actual message-processing behavior.
- Partially addressed (2026-08-30): `Tests/AWSRedrive.Test.Integration/KafkaSinkTests.cs` now covers the Kafka **happy** paths end to end against a real broker — produce, Snappy compression, and the fact that SQS attributes are not carried across. The Kafka **failure** branches (timeout, `ProduceException`, non-persisted status) remain unverified, as does everything in the PowerShell processor: `RedriveScript` is excluded from the integration suite by decision.
- Files: `Projects/AWSRedrive/PowershellMessageProcessor.cs`, `Projects/AWSRedrive/KafkaMessageProcessor.cs`
- Risk: Error-handling branches (script not found, script throws, script produces warnings; Kafka timeout, `ProduceException`, non-persisted status) could regress silently.
- Priority: Medium — these are less commonly used redrive paths than HTTP but still production-critical for the queues that rely on them.

**`async void` delete-failure path is untested:**
- What's not tested: There is no test verifying behavior when `AwsQueueClient.DeleteMessage` throws asynchronously after the SQS call is issued (see Known Bugs / Tech Debt above regarding `async void`).
- Partially addressed (2026-08-30): `FailureAndRetryTests.ASuccessfullyDeliveredMessageIsRemovedFromItsQueue` exercises the delete path for real — it asserts the queue drains and the message is not redelivered. That covers the success path only; the failure path is still unverified.
- Files: `Projects/AWSRedrive/AwsQueueClient.cs:105-119`, `Tests/AWSRedrive.Tests.Unit/AwsQueueClientTests.cs`
- Risk: A silent process crash or missed error metric on delete failure could go unnoticed until it happens in production.
- Priority: High — directly tied to a real correctness bug identified above.

**Duplicate-alias / malformed-config edge cases untested for the dashboard merge path:**
- What's not tested: `DashboardServer.GetStatus()`'s `ToDictionary(c => c.Alias, ...)` call assumes unique aliases across active processors; no test in `Tests/AWSRedrive.Tests.Unit/DashboardServerTests.cs` covers duplicate-alias configs causing an unhandled `ArgumentException`.
- Files: `Projects/AWSRedrive/DashboardServer.cs:216-262`, `Tests/AWSRedrive.Tests.Unit/DashboardServerTests.cs`
- Risk: A misconfigured `config.json` with duplicate aliases could take down the entire dashboard API (`/api/status`, `/api/stream`) at runtime with no test coverage warning of this failure mode.
- Priority: Medium — pair with adding the validator rule described in Fragile Areas.

---

*Concerns audit: 2026-08-28*
