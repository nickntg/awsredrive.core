# Testing Patterns

**Analysis Date:** 2026-08-28

## Test Framework

**Runner:**
- xUnit 2.9.2 (`xunit` + `xunit.runner.visualstudio` 2.8.2 packages) in both test projects.
- Config: no custom `xunit.runner.json`; defaults are used. Project config: `Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj`, `Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj`.
- Both test projects target `net8.0` and reference `Microsoft.NET.Test.Sdk` 17.11.1.
- `SonarQubeTestProject=true` is set in the unit test csproj for SonarQube test-project classification.

**Mocking Library:**
- FakeItEasy 8.3.0 (unit test project only — the integration test project has no mocking library, it exercises real objects).

**Assertion Library:**
- xUnit's built-in `Assert` class (`Assert.Equal`, `Assert.True`, `Assert.NotNull`, `Assert.Contains`, `Assert.Single`, `Assert.Empty`, `Assert.ThrowsAny<Exception>`, `Assert.DoesNotContain`).

**Run Commands:**
```bash
dotnet test Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj -c Debug   # unit tests (via Makefile: make test)
dotnet watch test --project Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj  # watch mode (make test-watch)
dotnet test Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj -c Debug  # integration tests (make integration-test)
dotnet test Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj --filter "Speed!=Slow"  # 19 of 28 (make integration-test-fast)
```
The integration suite needs its container stack running first — `integration-infrastructure/start.ps1`, torn down with `stop.ps1` (`make integration-up` / `make integration-down`). If it is not up, `StackPreflight` fails the run once with instructions rather than letting every test time out on a socket.

No coverage tooling (Coverlet/ReportGenerator) or CI pipeline config detected in the repo — coverage is not automated or enforced.

## Test File Organization

**Location:**
- Two separate test projects:
  - `Tests/AWSRedrive.Tests.Unit/` — fast, isolated unit tests using fakes. Flat, except `Helpers/`.
  - `Tests/AWSRedrive.Test.Integration/` — end-to-end tests against a running container stack. Test files are flat; shared clients and fixtures live in `Infrastructure/`.
- Test projects are NOT co-located with source; source lives under `Projects/AWSRedrive/`, tests live entirely under `Tests/`.

**Naming:**
- One test class per production class: `<ClassName>Tests.cs` (e.g. `Orchestrator.cs` → `OrchestratorTests.cs`, `AwsQueueClient.cs` → `AwsQueueClientTests.cs`).
- Test method names use `MethodOrScenario_Condition_ExpectedResult` style for behavior-focused tests (e.g. `GetLogLevel_BeforeStart_ReturnsNull`, `GetMessage_WithNullMessageAttributes_ReturnsMessageWithEmptyAttributes`) OR plain descriptive PascalCase sentences for simpler/older-style tests (e.g. `NoQueueUrl`, `StopsOneProcessor`, `VerifyGetRequestUrlConstruction`). Prefer the `Method_Condition_Result` style for new unit tests — it is more descriptive and used in the most recently added unit test files.
- Integration tests deliberately use full-sentence names describing observable behaviour rather than a method under test, because no single method is the subject: `PostIsTheDefaultVerbAndTheBodyArrivesVerbatim`, `AMessageRejectedPastMaxReceiveCountLandsInTheDeadLetterQueue`, `SqsMessageAttributesAreNotCarriedToKafka`.

**Structure:**
```
Tests/
├── AWSRedrive.Tests.Unit/
│   ├── AWSRedrive.Tests.Unit.csproj
│   ├── Helpers/
│   │   └── SimpleConfigurationReader.cs      # hand-written fake of IConfigurationReader
│   ├── OrchestratorTests.cs
│   ├── AwsQueueClientTests.cs                # also defines a local `TestableAwsQueueClient` subclass
│   ├── ConfigurationChangeManagerTests.cs
│   ├── ConfigurationEntryValidatorTests.cs
│   ├── DashboardServerTests.cs               # spins up a real DashboardServer on a real port
│   ├── HttpMessageProcessorTests.cs
│   └── ... (one file per production class)
└── AWSRedrive.Test.Integration/
    ├── AWSRedrive.Test.Integration.csproj
    ├── integrationsettings.json              # endpoints; every value env-var overridable
    ├── Infrastructure/
    │   ├── IntegrationTest.cs                # base: sink + queue clients, correlation helpers
    │   ├── TestEnvironment.cs                # settings + an IAmazonSQS pointed at Floci
    │   ├── SinkClient.cs                     # WaitForDeliveryAsync / AssertNoDeliveryAsync
    │   ├── TestQueueClient.cs                # send, queue depth, DLQ reads
    │   ├── RecordedRequest.cs                # one observed delivery, HTTP or Kafka
    │   └── StackPreflight.cs                 # assembly fixture + [Collection("integration")]
    ├── HttpVerbTests.cs                      # one file per behaviour area, not per class
    ├── AuthenticationTests.cs
    ├── HeaderPropagationTests.cs
    ├── FailureAndRetryTests.cs               # [Trait("Speed","Slow")]
    ├── TlsTests.cs
    ├── KafkaSinkTests.cs
    ├── ConfigurationReloadTests.cs           # [Trait("Speed","Slow")]
    └── DashboardTests.cs
```

## Test Structure

**Suite Organization:**
```csharp
public class OrchestratorTests
{
    private Orchestrator CreateOrchestrator()   // private factory helper builds the SUT with fakes
    {
        var configReader = A.Fake<IConfigurationReader>();
        var queueClientFactory = A.Fake<IQueueClientFactory>();
        // ... fake every constructor dependency
        return new Orchestrator(configReader, queueClientFactory, ...);
    }

    [Fact]
    public void GetLogLevel_BeforeStart_ReturnsNull()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.GetLogLevel("any-alias");

        // Assert
        Assert.Null(result);
    }
}
```

**Patterns:**
- Each test class typically has one or more small private factory helpers (`CreateOrchestrator()`, `CreateSettings(int port)`, `CreateLogger()`) to reduce Arrange-block duplication.
- `// Arrange` / `// Act` / `// Assert` comments are used in newer/more thorough tests; older/simpler tests omit them and just have 2-4 lines of plain code.
- Tests that start background work (`orchestrator.Start()`, `server.Start()`) always stop it in a `finally` block to avoid leaking threads/ports across tests: 
  ```csharp
  orchestrator.Start();
  try { /* act + assert */ }
  finally { orchestrator.Stop(); }
  ```
- `[Theory]` + `[InlineData(...)]` used for parameterized variants of the same behavior (verb selection, boolean flags): see `HttpMessageProcessorTests.VerifyCorrectHttpVerb`, `ConfigurationEntryValidatorTests.OnlyOneRedriveDestinationAllowed`.
- Tests that assert "no exception thrown" use `Record.Exception(() => ...)` + `Assert.Null(exception)` rather than a bare unguarded call (e.g. `OrchestratorTests.GetLogLevel_BeforeStart_DoesNotThrow`, `DashboardServerTests.Constructor_DoesNotThrow`).
- Global/shared static state used by production code (`MetricsStore`) is explicitly cleaned up at the end of a test via `MetricsStore.Remove(alias)` in a `finally` block, and tests that touch it generate a unique alias (`"metrics-test-alias-" + Guid.NewGuid()`) to avoid cross-test interference (`DashboardServerTests.ApiStatus_IncludesMetrics`).

## Mocking

**Framework:** FakeItEasy (`A.Fake<T>()`, `A.CallTo(...)`).

**Patterns:**
```csharp
// Loose fake (default) - unset members return defaults, no verification required
var configReader = A.Fake<IConfigurationReader>();

// Strict fake - every call must be explicitly configured or the fake throws
var mockProcessor = A.Fake<IQueueProcessor>(x => x.Strict());
A.CallTo(() => mockProcessor.Configuration).Returns(GetOneConfigurationEntry("#1", true));
A.CallTo(() => mockProcessor.Stop()).DoesNothing();
A.CallTo(() => mockProcessor.Equals(A<object>.Ignored)).CallsBaseMethod(); // needed for List.Find/Remove to work on strict fakes

// Argument matchers
A.CallTo(() => mockSqsClient.ReceiveMessageAsync(
        A<ReceiveMessageRequest>.Ignored,
        A<CancellationToken>.Ignored))
    .Returns(Task.FromResult(response));

// Verification (used with strict fakes to assert interaction counts)
A.CallTo(() => mockProcessor.Stop()).MustHaveHappenedOnceExactly();
A.CallTo(() => mockClient.Init()).MustHaveHappenedOnceExactly();
A.CallTo(() => mockProcessor.Configuration).MustHaveHappenedOnceOrMore();
```

**What to Mock:**
- All constructor-injected interfaces (`IConfigurationReader`, `IQueueClientFactory`, `IMessageProcessorFactory`, `IQueueProcessorFactory`, `IConfigurationChangeManager`, `IQueueProcessor`, `IQueueClient`, `IAmazonSQS`) are faked with `A.Fake<T>()` in unit tests — never hit real AWS/network/process resources.
- Use `.Strict()` fakes when the test needs to assert exact call counts/interaction order (state-transition/orchestration tests like `ConfigurationChangeManagerTests`); use loose (default) fakes when only the return value matters and interaction verification isn't the point (`OrchestratorTests`).
- When a class needs to intercept a `protected`/internal field (e.g. injecting a fake `IAmazonSQS` into `AwsQueueClient`), create a minimal testable subclass in the test file rather than modifying production code for testability: `TestableAwsQueueClient : AwsQueueClient` (`Tests/AWSRedrive.Tests.Unit/AwsQueueClientTests.cs:235-241`).
- For simple interfaces with a single trivial responsibility (`IConfigurationReader`), a hand-written fake class is preferred over `A.Fake<T>()` for readability: `Tests/AWSRedrive.Tests.Unit/Helpers/SimpleConfigurationReader.cs`.

**What NOT to Mock:**
- Pure data/model classes (`ConfigurationEntry`, `DashboardSettings`) are constructed directly with object initializers — never faked.
- Validators (`ConfigurationEntryValidator`) and processors under direct test (`HttpMessageProcessor`) are instantiated as real objects, not faked, since they are the system under test.
- Real network/server classes are sometimes deliberately exercised for real rather than mocked when testing integration-level behavior — `DashboardServerTests` starts a real Kestrel server on a real localhost port and issues a real `HttpClient` GET request.
- The integration project mocks **nothing**. It runs the real published redrive binary in a container against a real SQS emulator, a real Kafka broker and a real HTTP endpoint. Adding a mocking library there would defeat its purpose.

## Fixtures and Factories

**Test Data:**
```csharp
// Local factory method inside the test class, parameterized for reuse across tests
private static ConfigurationEntry GetOneConfigurationEntry(string alias, bool active) => new ConfigurationEntry
{
    Alias = alias,
    Active = active
};

private static DashboardSettings CreateSettings(int port) => new DashboardSettings
{
    Enabled = true,
    Port = port,
    RefreshIntervalMs = 5000
};
```

**Location:**
- No shared/global fixture project or `TestData` folder — each test class defines its own small private static factory methods for the data it needs, inline in the same file.
- The one shared fake implementation (`SimpleConfigurationReader`) lives in `Tests/AWSRedrive.Tests.Unit/Helpers/` and is reused across several test classes (`ConfigurationChangeManagerTests`, `DashboardServerTests`).
- Each test in `DashboardServerTests` uses a unique port number (5001, 5002, 5003, ...) to allow parallel test execution without port collisions.

## Coverage

**Requirements:** None enforced — no coverage tooling (Coverlet, dotnet-coverage) referenced in any `.csproj`, no coverage gate in `Makefile`, and no CI workflow files present in the repo.

**View Coverage:**
Not applicable — no coverage command configured. To add coverage, `dotnet test --collect:"XPlat Code Coverage"` would need to be wired into the `Makefile` `test` target.

## Test Types

**Unit Tests:**
- Located in `Tests/AWSRedrive.Tests.Unit/`. Scope: single class in isolation, all dependencies faked via FakeItEasy. Covers config validation (`ConfigurationEntryValidatorTests`), orchestration state machine (`OrchestratorTests`, `ConfigurationChangeManagerTests`), HTTP request construction (`HttpMessageProcessorTests`), SQS message mapping (`AwsQueueClientTests`), metrics (`MetricsStoreTests`, `MetricsSettingsProviderTests`, `QueueMetricsTests`), dashboard HTTP endpoints (`DashboardServerTests` — technically exercises a real embedded server, but is kept in the unit project because it doesn't call external services).

**Integration Tests:**
- Located in `Tests/AWSRedrive.Test.Integration/`. Scope: the whole redrive process, running as a container, driven by real SQS messages. 28 tests across eight files covering HTTP verb selection, the three authentication modes, attribute and SNS-envelope header propagation, timeout/500/DLQ retry behaviour, TLS certificate handling, the Kafka destination, runtime config reload, and dashboard metrics. PowerShell delivery (`RedriveScript`) is excluded by decision.
- Prerequisite: the container stack in `integration-infrastructure/` must be running. The test project has no Testcontainers reference and no Docker orchestration — the lifecycle is manual so the stack can be started from a terminal and the tests run and debugged from Visual Studio.
- **Isolation model.** `ConfigurationReader` reads `config.json` from disk and `Orchestrator` re-reads it only every 60 seconds, so per-test configuration is impractical. Instead the 17 aliases in `integration-infrastructure/redrive/config.json` are keyed by *configuration shape* and shared between tests, and each test isolates itself with a fresh correlation GUID that the sink indexes deliveries by. No test resets shared state and none depends on ordering.
- The exception is `ConfigurationReloadTests`: each of its tests owns an alias nothing else touches (`it-inactive`, `it-activatable`, `it-removable`, `it-reload`), because restoring the baseline config in teardown does not reach redrive for up to another 60 seconds.
- Slow tests carry `[Trait("Speed","Slow")]`; `--filter "Speed!=Slow"` selects 19 of the 28.
- See `integration-infrastructure/README.md` for the stack itself, and `docs/superpowers/specs/2026-08-30-integration-test-suite-design.md` for the design rationale.

**E2E Tests:** Covered by the integration project above — there is no separate E2E project.

## Common Patterns

**Async Testing:**
```csharp
[Fact]
public async Task Dashboard_ReturnsHtml()
{
    var server = new DashboardServer(configReader, CreateSettings(5003));
    server.Start();
    await Task.Delay(1000);   // give Kestrel time to bind before issuing requests

    try
    {
        using var client = new HttpClient();
        var response = await client.GetAsync("http://localhost:5003/");
        Assert.True(response.IsSuccessStatusCode);
    }
    finally
    {
        server.Stop();
    }
}
```
FakeItEasy async members are configured with `.Returns(Task.FromResult(response))` rather than `async`/`await` inside the setup.

**Error Testing:**
```csharp
// Assert NO exception is thrown (guard against regressions in error handling)
var exception = Record.Exception(() => orchestrator.GetLogLevel("any-alias"));
Assert.Null(exception);
```

---

*Testing analysis: 2026-08-28. Integration suite sections revised 2026-08-30.*
