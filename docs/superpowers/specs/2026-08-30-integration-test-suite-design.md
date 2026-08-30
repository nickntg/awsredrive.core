# Docker-Based Integration Test Suite — Design

**Date:** 2026-08-30
**Branch:** `17-create-an-integration-test-suite`
**Status:** Approved for planning

## Goal

Replace the two placeholder tests in `Tests/AWSRedrive.Test.Integration` with a suite that
exercises AWSRedrive end to end against real containers: an SQS emulator feeding it, a
recording HTTP service and a Kafka broker receiving from it.

The suite must confirm that a message put on an SQS queue arrives at redrive's configured
destination with the right HTTP method, headers, authentication and body — and that redrive
behaves correctly when the destination fails, stalls, or presents an untrusted certificate.

## Scope

**In scope.** SQS as the only input. HTTP and Kafka as destinations. Every
`ConfigurationEntry` knob that affects delivery: verb selection, the three authentication
modes, attribute-to-header propagation, SNS envelope unpacking, timeouts, certificate
handling, Kafka compression, `Active`, and runtime config reload. Dashboard metrics and DLQ
auto-discovery.

**Out of scope.** `RedriveScript` / PowerShell delivery — excluded by explicit decision.
Kafka as an *input* source; redrive cannot consume from Kafka and no product change is part
of this work. Any change to `Projects/` source.

## Operating Model

Infrastructure lifecycle is manual and separate from the test run.

```
PS> cd integration-infrastructure
PS> ./start.ps1          # build + up + wait for healthy
   ... switch to Visual Studio, run tests from the test runner ...
PS> ./stop.ps1           # down + remove volumes and locally-built images
```

The test project contains **no** Testcontainers reference and no Docker orchestration. It is
plain xUnit that talks to endpoints on fixed host ports. This is a hard constraint, not a
default.

## Architecture

### Container topology

Six services on one compose network, in `integration-infrastructure/docker-compose.yml`.
Three ports published to the host.

| Service | Image | Host port | Role |
|---|---|---|---|
| `floci` | `floci/floci:latest` | 4566 | SQS emulator. Tests send messages here; redrive polls it. |
| `floci-init` | `amazon/aws-cli` | — | One-shot. Retries `list-queues` until Floci answers, then creates every queue and DLQ with its redrive policy and visibility timeout. Exits 0. |
| `kafka` | `apache/kafka` (KRaft, no ZooKeeper) | 29092 | Redrive's Kafka destination. Internal listener `kafka:9092`. |
| `kafka-init` | `apache/kafka` | — | One-shot. Creates the two test topics so the sink can subscribe at startup rather than depending on auto-creation. |
| `sink` | built from `sink/Dockerfile` | 8080, 8443 | Records what arrives over HTTP and off the Kafka topics; serves recordings back to tests. |
| `redrive` | built from repo `Dockerfile.image`, target `console-image` | 5000 | System under test. Dashboard exposed for metrics assertions. |

`redrive` depends on `floci-init` and `kafka-init` with `service_completed_successfully`, so
queues and topics exist before it first reads config.

Floci gets no compose healthcheck. Its image is a Quarkus native build whose health endpoint
and available shell utilities are unverified; `floci-init`'s retry loop is the readiness gate
instead, which works regardless of what the image contains.

### Directory layout

```
integration-infrastructure/
  README.md                   # the two commands and the port table
  start.ps1
  stop.ps1
  docker-compose.yml
  .env                        # ports and image tags
  redrive/
    config.json               # all aliases, committed; bind-mounted into redrive AND sink
    appsettings.json          # dashboard on, metrics interval 5s
    NLog.config               # console target
  floci/
    seed.sh                   # queue + DLQ creation, run by floci-init
  kafka/
    seed.sh                   # topic creation, run by kafka-init
  sink/
    Dockerfile
    app.py
    requirements.txt
    certs/generate.sh         # self-signed cert, CN=sink, SAN DNS:sink,DNS:localhost
```

Everything infrastructure-related lives here, including the sink's source. Nothing
infrastructure-related goes under `Tests/` or `Projects/`.

### The sink service

A single-file Python application (FastAPI + `confluent-kafka`) doing three jobs.

**1. Recording.** A catch-all route accepts any method on `/http/*` and `/secure/*` and
records method, path, query string, headers, raw body and arrival timestamp. A background
Kafka consumer subscribed to the test topics records messages the same way.

**2. Serving recordings.** Tests query by correlation id, never by index or order:

```
GET    /recorded/{correlation_id}   -> [] or [{ transport, method, path, query,
                                                headers, body, topic, at }]
DELETE /recorded/{correlation_id}   -> drop (housekeeping only; tests do not need it)
GET    /health                      -> readiness for the compose healthcheck
```

The correlation id is extracted, in order: an `X-Correlation-Id` header, a `correlation`
field in a JSON body, or a `correlation` query parameter. The third case covers `UseGET`,
where redrive unwraps the body into query parameters and sends no body at all.

**3. Configured behaviour** for negative tests, driven by the request path and query string
so it can be encoded directly in an alias's `RedriveUrl`:

| Path | Behaviour |
|---|---|
| `/http/post`, `/http/put`, `/http/delete`, `/http/get` | Record, return 200 |
| `/http/slow?delay=6000` | Sleep, then 200 — outlives the alias's 2s `Timeout` |
| `/http/fail?status=500` | Record the attempt, return 500 |
| `/http/sns` | Record, return 200 |
| `/secure/token` | 401 unless `Authorization` matches the expected value |
| `/secure/apikey` | 401 unless `x-api-key` matches |
| `/secure/basic` | 401 unless Basic credentials match |
| `/http/tls`, `/http/tls-strict` | HTTPS-only, on 8443 |

Attempts are recorded *before* a rejection is returned, so retry tests can count them.

**4. Config administration.** `redrive/config.json` is bind-mounted read-write into the sink
so that reload tests never need filesystem knowledge:

```
GET    /admin/redrive-config   -> current alias list
POST   /admin/redrive-config   -> replace the file
DELETE /admin/redrive-config   -> restore the committed baseline
```

The baseline is copied into the image at build time, so `DELETE` always restores a known
state even after a test crashes mid-run.

Python is chosen over C# because the HTTP recorder and the Kafka consumer fit in one readable
file with no build step, and the image stays small. This is the only non-.NET code in the
repository.

## Test Isolation

Two properties of the system under test drive the whole approach:

1. `config.json` is read from disk by `ConfigurationReader` at a fixed relative path.
2. `Orchestrator.StartProcessing` re-reads it only **every 60 seconds**.

Per-test configuration is therefore impossible without paying a minute per test.

**Aliases are keyed by configuration shape, not by test.** Sixteen aliases, each with its own
queue and its own sink path, cover every combination redrive can be configured into. They are
committed once and shared by all tests.

**Tests isolate by correlation GUID.** Each test sends a message carrying a fresh GUID, then
polls `GET /recorded/{guid}` until the expected record appears or a timeout elapses. No test
resets shared state, no test depends on ordering, and several tests can share an alias and run
concurrently without observing each other's traffic.

**Wait budget.** `AwsQueueClient` uses `WaitTimeSeconds = 20`, so a message published while
redrive is mid-poll is returned immediately, but a worst-case wait is one long-poll cycle.
Delivery assertions poll for 30 seconds. Reload assertions poll for 90 seconds.

**Fast redelivery.** Queues used by failure tests are created with `VisibilityTimeout = 5`, so
a message rejected by the sink returns to the queue in five seconds rather than the default
thirty. This keeps the DLQ test to roughly fifteen seconds.

## Alias Configuration

All entries share `ServiceUrl: http://floci:4566`, `Region: us-east-1`, and
`AccessKey`/`SecretKey` of `test`/`test`. `Active` is `true` unless noted.

| Alias | Queue | Configuration |
|---|---|---|
| `it-http-post` | `it-http-post` | `RedriveUrl: http://sink:8080/http/post` |
| `it-http-put` | `it-http-put` | as above + `UsePUT` |
| `it-http-delete` | `it-http-delete` | as above + `UseDelete` |
| `it-http-get` | `it-http-get` | as above + `UseGET` |
| `it-auth-token` | `it-auth-token` | `/secure/token`, `AuthToken` set |
| `it-gateway-token` | `it-gateway-token` | `/secure/apikey`, `AwsGatewayToken` set |
| `it-basic-auth` | `it-basic-auth` | `/secure/basic`, `BasicAuthUserName` + `BasicAuthPassword` |
| `it-sns-unpack` | `it-sns-unpack` | `/http/sns`, `UnpackAttributesAsHeaders: true` |
| `it-timeout` | `it-timeout` | `/http/slow?delay=6000`, `Timeout: 2000`; queue has a DLQ, `maxReceiveCount: 3` |
| `it-failing` | `it-failing` | `/http/fail?status=500`; queue has a DLQ, `maxReceiveCount: 2` |
| `it-https-lax` | `it-https-lax` | `https://sink:8443/http/tls`, `IgnoreCertificateErrors: true` |
| `it-https-strict` | `it-https-strict` | `https://sink:8443/http/tls-strict`, `IgnoreCertificateErrors: false`; queue has a DLQ, `maxReceiveCount: 3` |
| `it-inactive` | `it-inactive` | `/http/inactive`, `Active: false` |
| `it-kafka-plain` | `it-kafka-plain` | `RedriveKafkaTopic: it-kafka-plain`, `KafkaBootstrapServers: kafka:9092` |
| `it-kafka-compressed` | `it-kafka-compressed` | as above + `UseKafkaCompression: true` |
| `it-reload` | `it-reload` | Queue seeded, alias **absent** from the committed config; added at runtime by a test |

Every failure-path queue has a dead letter queue so poison messages drain instead of retrying
for the lifetime of the stack.

## Test Matrix

Twenty-eight tests across eight files.

### HttpVerbTests
1. POST is the default; body arrives verbatim.
2. `UsePUT` produces a PUT with the body intact.
3. `UseDelete` produces a DELETE that still carries the JSON body.
4. `UseGET` unwraps a JSON body into query parameters and sends no body.
5. `UseGET` with a non-JSON body still delivers, without query parameters.
6. A ~200KB Unicode payload arrives byte-for-byte identical.

### AuthenticationTests
7. `AuthToken` arrives as the `Authorization` header, value unchanged.
8. `AwsGatewayToken` arrives as the `x-api-key` header.
9. Basic credentials are accepted by a sink that 401s on anything else.

### HeaderPropagationTests
10. SQS message attributes become HTTP headers.
11. Reserved names (`Host`, `Content-Type`, `Accept`, `Accept-Encoding`, `Content-Length`)
    sent as SQS attributes are not propagated.
12. `UnpackAttributesAsHeaders` turns an SNS envelope's `MessageAttributes` into headers.
13. `UnpackAttributesAsHeaders` against a body that is not an SNS envelope still delivers.

### FailureAndRetryTests — `[Trait("Speed","Slow")]`
14. A sink that stalls past `Timeout` causes redelivery; at least two attempts are recorded.
15. A sink returning 500 causes redelivery.
16. A message rejected `maxReceiveCount` times lands in the dead letter queue.
17. A successfully delivered message is removed from its queue.

### TlsTests
18. Self-signed HTTPS with `IgnoreCertificateErrors: true` delivers.
19. The same endpoint with the flag `false` never delivers, within a bounded wait.

### KafkaSinkTests
20. An SQS message reaches the configured Kafka topic with its payload intact.
21. The same holds with Snappy compression enabled.
22. SQS message attributes are *not* carried to Kafka — pins `KafkaMessageProcessor`'s
    current behaviour, which ignores the attributes argument.

### ConfigurationReloadTests — `[Trait("Speed","Slow")]`
23. An `Active: false` alias never drains its queue.
24. Flipping it to `true` drains the queue within ~70 seconds.
25. A brand-new alias added at runtime starts delivering within ~70 seconds.
26. A removed alias stops delivering.

Each of these restores the baseline config via `DELETE /admin/redrive-config` in teardown.

### DashboardTests
27. `/api/status` reports `messagesReceived` and `messagesSent` climbing for an alias after
    traffic.
28. `DlqUrl`, auto-discovered from the SQS `RedrivePolicy`, appears in `/api/status` for a
    queue that has one.

## Test Project

`Tests/AWSRedrive.Test.Integration` keeps its name and location. `IntegrationTests.cs` is
deleted — both of its tests assert only that a request to a nonexistent host throws.

The csproj gains `Microsoft.NET.Test.Sdk` (absent today, which is why the project has never
run under any runner) and `AWSSDK.SQS`. It gains no Docker-related package.

```
Tests/AWSRedrive.Test.Integration/
  integrationsettings.json      # endpoints; every value overridable by environment variable
  Infrastructure/
    TestEnvironment.cs          # settings, plus an IAmazonSQS pointed at Floci on localhost
    SinkClient.cs               # WaitForDelivery(correlationId, timeout) -> RecordedRequest[]
    TestQueueClient.cs          # send with attributes, read depth, drain a DLQ
    StackPreflight.cs           # assembly fixture
  HttpVerbTests.cs
  AuthenticationTests.cs
  HeaderPropagationTests.cs
  FailureAndRetryTests.cs
  TlsTests.cs
  KafkaSinkTests.cs
  ConfigurationReloadTests.cs
  DashboardTests.cs
```

`StackPreflight` runs once per assembly and probes the sink, Floci and the redrive dashboard.
If nothing answers it fails with a single sentence naming `integration-infrastructure/start.ps1`,
rather than letting twenty-eight tests each time out on a socket.

## Scripts

**`start.ps1`** — verifies the Docker daemon responds; runs `docker compose up -d --build
--wait` so it returns only once every healthcheck passes and both init containers have
exited 0; prints the host port table. On failure it dumps the failing service's logs and exits
non-zero.

**`stop.ps1`** — runs `docker compose down -v --rmi local --remove-orphans`, scoped to this
compose project. A `-All` switch additionally runs `docker system prune -f` to clear the
builder cache; that path prompts for confirmation first.

## Known Risks

**Queue URL host mismatch.** Redrive resolves `floci`; tests resolve `localhost`. Both are
handled by writing `QueueUrl` explicitly rather than relying on what `CreateQueue` returns —
`http://floci:4566/000000000000/it-http-post` in `config.json`, `http://localhost:4566/...` in
the test settings. `AwsQueueClient.GetDlqUrlAsync` already builds DLQ URLs as
`{ServiceUrl}/{account}/{queueName}`, confirming this path shape is what the code expects.
Verify during implementation that Floci routes by path and ignores the host component.

**Region resolution.** `AwsQueueClient.Init` sets `ServiceURL` *or* `RegionEndpoint`, never
both, so `Region` in an alias is ignored once `ServiceUrl` is set. AWS SDK v4 may still require
a region for request signing. Mitigation: set `AWS_REGION` and `AWS_DEFAULT_REGION` on the
`redrive` container.

**Floci fidelity.** Floci is presented as a LocalStack drop-in but is a different
implementation. Message attributes, `RedrivePolicy` and DLQ mechanics need to be confirmed
early — tests 10–13, 16 and 28 all depend on them. This is the first thing to verify in
implementation; if DLQ redrive is unsupported, tests 16 and 28 get cut and the affected
queues lose their redrive policies.

**`UseGET` query composition.** Redrive builds the request from `uri.PathAndQuery` and then
adds parameters. For `it-http-get` the `RedriveUrl` carries no query string, so no conflict
arises; for the behaviour-carrying paths (`?delay=`, `?status=`) no parameters are added.
The combination is deliberately never exercised.

## Explicitly Not Done

- No changes to anything under `Projects/`.
- No PowerShell/`RedriveScript` coverage.
- No CI wiring. The repository has no `.github/workflows`, and adding one is a separate
  decision.
- No changes to the unit test project.
