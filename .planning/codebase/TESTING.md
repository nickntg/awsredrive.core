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
dotnet test Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj -c Debug  # integration tests (not wired into Makefile directly)
```
No coverage tooling (Coverlet/ReportGenerator) or CI pipeline config detected in the repo — coverage is not automated or enforced.

## Test File Organization

**Location:**
- Two separate test projects, both flat (no subfolders except `Helpers/`):
  - `Tests/AWSRedrive.Tests.Unit/` — fast, isolated unit tests using fakes.
  - `Tests/AWSRedrive.Test.Integration/` — tests that make real outbound calls (e.g. real HTTP requests to unreachable hosts) and are expected to be slower/more fragile.
- Test projects are NOT co-located with source; source lives under `Projects/AWSRedrive/`, tests live entirely under `Tests/`.

**Naming:**
- One test class per production class: `<ClassName>Tests.cs` (e.g. `Orchestrator.cs` → `OrchestratorTests.cs`, `AwsQueueClient.cs` → `AwsQueueClientTests.cs`).
- Test method names use `MethodOrScenario_Condition_ExpectedResult` style for behavior-focused tests (e.g. `GetLogLevel_BeforeStart_ReturnsNull`, `GetMessage_WithNullMessageAttributes_ReturnsMessageWithEmptyAttributes`) OR plain descriptive PascalCase sentences for simpler/older-style tests (e.g. `NoQueueUrl`, `StopsOneProcessor`, `VerifyGetRequestUrlConstruction`). Prefer the `Method_Condition_Result` style for new tests — it is more descriptive and used in the most recently added test files.

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
    └── IntegrationTests.cs
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
- Real network/server classes are sometimes deliberately exercised for real rather than mocked when testing integration-level behavior — `DashboardServerTests` starts a real Kestrel server on a real localhost port and issues a real `HttpClient` GET request; `IntegrationTests` performs a real outbound HTTP call expected to fail with a network-level exception.

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
- Located in `Tests/AWSRedrive.Test.Integration/`. Scope: exercises `HttpMessageProcessor.ProcessMessage` end-to-end against real (unreachable) hosts to confirm real network exceptions propagate correctly (`IntegrationTests.cs`). No mocking library referenced in this project — it is intentionally "real" at the edges.

**E2E Tests:** Not used.

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
// Assert an exception is thrown (integration test, real network failure)
Assert.ThrowsAny<Exception>(() =>
    o.ProcessMessage("test", new Dictionary<string, string>(),
        new ConfigurationEntry { RedriveUrl = "http://nonehost.com/post/here?parm=test" },
        CreateLogger()));

// Assert NO exception is thrown (guard against regressions in error handling)
var exception = Record.Exception(() => orchestrator.GetLogLevel("any-alias"));
Assert.Null(exception);
```

---

*Testing analysis: 2026-08-28*
