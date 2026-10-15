# Integration test infrastructure

Everything the integration suite needs, as Docker containers. Start it from
here, then run the tests from Visual Studio or the CLI.

```powershell
cd integration-infrastructure
./start.ps1                                   # build and start; waits until ready
dotnet test --project ../Tests/AWSRedrive.Test.Integration
./stop.ps1                                    # containers, volumes and built images
```

`./stop.ps1 -All` additionally prunes unused Docker data machine-wide. It asks
first.

The test project knows nothing about Docker. It talks to fixed host ports, and
fails with a single sentence pointing at `start.ps1` if nothing answers.

## What is running

| Service | Host address | Role |
|---|---|---|
| `floci` | http://localhost:4566 | SQS emulator. Tests send messages here. |
| `kafka` | localhost:29092 | Redrive's Kafka destination. |
| `sink` | http://localhost:8080, https://localhost:8443 | Records every delivery and serves it back to tests. |
| `redrive` | http://localhost:5000 | The system under test, and its dashboard. |
| `dozzle` | http://localhost:9999 | Web log viewer for the stack. Not part of the system under test. |

`floci-init` and `kafka-init` are one-shot containers that create the queues and
topics, then exit. They are the readiness gate — `redrive` waits for both, plus a
healthy `sink`, before it starts.

Ports live in `.env`. Change them there and both the compose file and
`start.ps1` follow; the test project needs a matching
`AWSREDRIVE_IT_SINKHTTPURL` (and friends) environment variable, or an edit to
`Tests/AWSRedrive.Test.Integration/integrationsettings.json`.

## Poking at it by hand

```powershell
curl.exe -s http://localhost:8080/recorded         # the last 100 deliveries
curl.exe -s http://localhost:8080/recorded/<guid>  # one correlation id
curl.exe -s http://localhost:5000/api/status       # redrive's own view
docker compose logs -f redrive
```

Or open <http://localhost:9999> for the same logs in a browser — Dozzle, live
tailing every container in the stack. It is filtered to this compose project, so
unrelated containers on the same daemon do not show up, and nothing in the test
suite depends on it: if it fails to start, `start.ps1` warns and carries on.

## How the tests stay isolated

Redrive reads `config.json` from disk and the `Orchestrator` only re-reads it
once a minute, so per-test configuration would cost a minute per test. Instead:

- **Aliases are keyed by configuration shape, not by test.** The 17 aliases in
  `redrive/config.json` cover every combination redrive supports — the four
  verbs, the three authentication modes, TLS with and without certificate
  validation, both Kafka variants, and an inactive entry. Several tests share an
  alias.
- **Tests isolate by correlation GUID.** Each test puts a fresh GUID in its
  message; the sink indexes deliveries by it, reading it from an
  `X-Correlation-Id` header, a `correlation` JSON field, or a `correlation` query
  parameter. That last one is what covers `UseGET`, where redrive unwraps the
  body into query parameters and sends no body at all.

The configuration-reload tests are the exception: each owns an alias nothing
else touches (`it-inactive`, `it-activatable`, `it-removable`, and `it-reload`,
which has a queue but no alias until a test adds one). Restoring the baseline in
teardown does not reach redrive for up to another 60 seconds, so a reload test
that mutated a shared alias would leave the next test running against a
configuration redrive has not caught up with yet.

Failure-path queues (`it-timeout`, `it-failing`, `it-https-strict`) have a
5 second visibility timeout and their own dead letter queue, so rejected
messages retry quickly and eventually drain instead of churning for the life of
the stack.

## The sink service

`sink/app.py` is a single-file FastAPI app doing four jobs:

1. **Recording** — a catch-all route under `/http/*` and `/secure/*` stores
   method, path, query, headers and body; a background thread does the same for
   the Kafka topics.
2. **Serving recordings** — `GET /recorded/{correlation_id}`.
3. **Behaviour for negative tests** — encoded in the URL so it can live in an
   alias's `RedriveUrl`: `?status=500` forces a failure, `?delay=6000` outlives
   the alias timeout, and the `/secure/*` routes 401 anything without the right
   credential. Attempts are recorded *before* a rejection, so retry tests can
   count them.
4. **Config administration** — `redrive/config.json` is mounted read-write into
   the sink as well as redrive, and `/admin/redrive-config` reads, replaces and
   restores it. This is how the reload tests change redrive's configuration
   without needing to know where the repository lives on disk.

The file is rewritten in place rather than renamed over. A rename would replace
the inode and silently break the bind mount into the redrive container.

## Changing the configuration

Edit `redrive/config.json`, then restart just redrive:

```powershell
docker compose restart redrive
```

Adding an alias also means adding its queue to `floci/seed.sh`, which needs a
full `./stop.ps1; ./start.ps1` — Floci holds queues in memory.

Note that `DELETE /admin/redrive-config` restores the copy of `config.json` that
was baked into the sink image when it was built, not whatever is on disk now. If
you edit the file, rebuild the sink (`./start.ps1`, or
`docker compose build sink`) so the reload tests restore what you expect.

## Design and plan

- `docs/superpowers/specs/2026-08-30-integration-test-suite-design.md`
- `docs/superpowers/plans/2026-08-30-integration-test-suite.md`
