# Docker-Based Integration Test Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a six-container Docker stack and a 28-test xUnit suite that exercises AWSRedrive end to end — SQS in, HTTP and Kafka out.

**Architecture:** Infrastructure lives entirely in `integration-infrastructure/` and is started by hand with `start.ps1`. A Python sink service records everything redrive delivers, over both HTTP and Kafka, and serves it back to tests keyed by a correlation GUID. The test project is plain xUnit with no Docker awareness — it talks to fixed host ports.

**Tech Stack:** Docker Compose, Floci (SQS emulator), Apache Kafka (KRaft), Python 3.12 + FastAPI + confluent-kafka, .NET 8 + xUnit + AWSSDK.SQS, PowerShell.

**Spec:** `docs/superpowers/specs/2026-08-30-integration-test-suite-design.md`

## Global Constraints

- All infrastructure code — compose file, Dockerfiles, sink source, seed scripts, redrive's `config.json` — goes under `integration-infrastructure/`. Nothing infrastructure-related under `Tests/` or `Projects/`.
- The test project must never reference Testcontainers or any Docker package.
- No changes to anything under `Projects/`.
- Floci endpoint inside the network: `http://floci:4566`. From the host: `http://localhost:4566`.
- AWS credentials everywhere: `AccessKey` / `SecretKey` = `test` / `test`, region `us-east-1`, account id `000000000000`.
- Kafka inside the network: `kafka:9092`. From the host: `localhost:29092`.
- Sink inside the network: `http://sink:8080`, `https://sink:8443`. From the host: `http://localhost:8080`, `https://localhost:8443`.
- Redrive dashboard: `http://localhost:5000`.
- Correlation id travels as an `X-Correlation-Id` header, a `correlation` JSON body field, or a `correlation` query parameter — the sink checks all three, in that order.
- Delivery assertions poll for 30 seconds. Config-reload assertions poll for 90 seconds.
- The sink writes `config.json` **in place** (truncate + write). It must never rename over it — that replaces the inode and silently breaks the bind mount into the redrive container.
- Target framework `net8.0`. xunit 2.9.2, matching the unit test project.

---

### Task 1: Compose skeleton, Floci, and queue seeding

**Files:**
- Create: `integration-infrastructure/.env`
- Create: `integration-infrastructure/docker-compose.yml`
- Create: `integration-infrastructure/floci/seed.sh`
- Create: `integration-infrastructure/start.ps1`
- Create: `integration-infrastructure/stop.ps1`
- Create: `.dockerignore` (repo root, if absent)

**Interfaces:**
- Consumes: nothing.
- Produces: a running `floci` on host port 4566 with 15 queues and 3 DLQs; `docker compose` project name `awsredrive-it`; `./start.ps1` and `./stop.ps1` entry points.

- [ ] **Step 1: Write `.env`**

```dotenv
COMPOSE_PROJECT_NAME=awsredrive-it

FLOCI_PORT=4566
SINK_HTTP_PORT=8080
SINK_HTTPS_PORT=8443
KAFKA_PORT=29092
REDRIVE_DASHBOARD_PORT=5000

AWS_ACCOUNT_ID=000000000000
AWS_REGION=us-east-1

FLOCI_IMAGE=floci/floci:latest
KAFKA_IMAGE=apache/kafka:3.9.0
AWSCLI_IMAGE=amazon/aws-cli:latest

EXPECTED_AUTH_TOKEN=Bearer it-integration-token
EXPECTED_API_KEY=it-integration-api-key
EXPECTED_BASIC_USER=it-user
EXPECTED_BASIC_PASSWORD=it-password
```

- [ ] **Step 2: Write `floci/seed.sh`**

Creates every queue the suite needs. Failure-path queues get a DLQ, `maxReceiveCount`, and a 5-second visibility timeout so redeliveries happen fast. The `until` loop is the readiness gate for Floci — no healthcheck on the Floci container itself.

```sh
#!/bin/sh
set -e

ENDPOINT="http://floci:4566"
ACCOUNT="${AWS_ACCOUNT_ID:-000000000000}"

export AWS_ACCESS_KEY_ID=test
export AWS_SECRET_ACCESS_KEY=test
export AWS_DEFAULT_REGION="${AWS_REGION:-us-east-1}"

echo "Waiting for Floci at $ENDPOINT ..."
i=0
until aws --endpoint-url "$ENDPOINT" sqs list-queues >/dev/null 2>&1; do
  i=$((i + 1))
  if [ "$i" -gt 120 ]; then
    echo "Floci did not become ready within 120s" >&2
    exit 1
  fi
  sleep 1
done
echo "Floci is ready."

create_plain() {
  echo "  queue $1"
  aws --endpoint-url "$ENDPOINT" sqs create-queue --queue-name "$1" >/dev/null
}

create_with_dlq() {
  name="$1"
  max="$2"
  dlq="${name}-dlq"

  echo "  queue $name (dlq=$dlq maxReceiveCount=$max)"
  aws --endpoint-url "$ENDPOINT" sqs create-queue --queue-name "$dlq" >/dev/null

  arn=$(aws --endpoint-url "$ENDPOINT" sqs get-queue-attributes \
    --queue-url "$ENDPOINT/$ACCOUNT/$dlq" \
    --attribute-names QueueArn \
    --query 'Attributes.QueueArn' --output text)

  cat > /tmp/attrs.json <<EOF
{
  "VisibilityTimeout": "5",
  "RedrivePolicy": "{\"deadLetterTargetArn\":\"$arn\",\"maxReceiveCount\":\"$max\"}"
}
EOF

  aws --endpoint-url "$ENDPOINT" sqs create-queue \
    --queue-name "$name" \
    --attributes file:///tmp/attrs.json >/dev/null
}

echo "Creating queues..."
for q in it-http-post it-http-put it-http-delete it-http-get \
         it-auth-token it-gateway-token it-basic-auth it-sns-unpack \
         it-inactive it-kafka-plain it-kafka-compressed it-reload; do
  create_plain "$q"
done

create_with_dlq it-timeout 3
create_with_dlq it-failing 2
create_with_dlq it-https-strict 3

echo "Seed complete:"
aws --endpoint-url "$ENDPOINT" sqs list-queues
```

- [ ] **Step 3: Write `docker-compose.yml` with the Floci services only**

Other services are added in later tasks. `amazon/aws-cli`'s entrypoint is `aws`, so it must be overridden to run a shell script.

```yaml
services:
  floci:
    image: ${FLOCI_IMAGE}
    container_name: awsredrive-it-floci
    ports:
      - "${FLOCI_PORT}:4566"
    environment:
      FLOCI_STORAGE_MODE: memory

  floci-init:
    image: ${AWSCLI_IMAGE}
    container_name: awsredrive-it-floci-init
    depends_on:
      - floci
    entrypoint: ["/bin/sh", "/seed/seed.sh"]
    environment:
      AWS_ACCOUNT_ID: ${AWS_ACCOUNT_ID}
      AWS_REGION: ${AWS_REGION}
    volumes:
      - ./floci/seed.sh:/seed/seed.sh:ro

networks:
  default:
    name: awsredrive-it
```

- [ ] **Step 4: Write `start.ps1`**

Redrive's and Floci's images have no shell, so readiness is probed from PowerShell over HTTP rather than by a compose healthcheck.

```powershell
#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

function Test-DockerRunning {
    try { docker info 2>&1 | Out-Null; return $LASTEXITCODE -eq 0 }
    catch { return $false }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "docker was not found on PATH. Install Docker Desktop and try again."
}
if (-not (Test-DockerRunning)) {
    Write-Error "The Docker daemon is not responding. Start Docker Desktop and try again."
}

$env:COMPOSE_PROJECT_NAME = 'awsredrive-it'

Write-Host "Starting the AWSRedrive integration stack..." -ForegroundColor Cyan
$upArgs = @('compose', 'up', '-d')
if (-not $NoBuild) { $upArgs += '--build' }
& docker @upArgs
if ($LASTEXITCODE -ne 0) { Write-Error "docker compose up failed." }

function Wait-ForHttp {
    param([string]$Name, [string]$Url, [int]$Timeout)
    Write-Host -NoNewline "  waiting for $Name ... "
    $deadline = (Get-Date).AddSeconds($Timeout)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -TimeoutSec 3 -UseBasicParsing `
                 -SkipCertificateCheck:$($Url.StartsWith('https')) -ErrorAction Stop
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) {
                Write-Host "ok" -ForegroundColor Green
                return $true
            }
        } catch { Start-Sleep -Milliseconds 700 }
    }
    Write-Host "TIMEOUT" -ForegroundColor Red
    return $false
}

function Wait-ForInitContainer {
    param([string]$Service, [int]$Timeout)
    Write-Host -NoNewline "  waiting for $Service to complete ... "
    $deadline = (Get-Date).AddSeconds($Timeout)
    while ((Get-Date) -lt $deadline) {
        $id = (docker compose ps -q $Service)
        if ($id) {
            $state = docker inspect -f '{{.State.Status}}:{{.State.ExitCode}}' $id 2>$null
            if ($state -like 'exited:0') { Write-Host "ok" -ForegroundColor Green; return $true }
            if ($state -like 'exited:*') {
                Write-Host "FAILED" -ForegroundColor Red
                docker compose logs $Service
                return $false
            }
        }
        Start-Sleep -Milliseconds 700
    }
    Write-Host "TIMEOUT" -ForegroundColor Red
    docker compose logs $Service
    return $false
}

$ok = Wait-ForInitContainer -Service 'floci-init' -Timeout $TimeoutSeconds
if (-not $ok) { & docker compose logs floci; Write-Error "Queue seeding did not complete." }

Write-Host ""
Write-Host "Stack is up." -ForegroundColor Green
Write-Host "  Floci (SQS)        http://localhost:$($env:FLOCI_PORT ?? 4566)"
Write-Host ""
Write-Host "Run the tests from Visual Studio, or:" -ForegroundColor Cyan
Write-Host "  dotnet test Tests/AWSRedrive.Test.Integration"
```

- [ ] **Step 5: Write `stop.ps1`**

```powershell
#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$All
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "docker was not found on PATH."
}

$env:COMPOSE_PROJECT_NAME = 'awsredrive-it'

Write-Host "Tearing down the AWSRedrive integration stack..." -ForegroundColor Cyan
& docker compose down -v --rmi local --remove-orphans

if ($All) {
    Write-Host ""
    Write-Host "-All was specified. This prunes ALL unused Docker data on this machine," -ForegroundColor Yellow
    Write-Host "not just this stack: stopped containers, unused networks, dangling images" -ForegroundColor Yellow
    Write-Host "and the build cache." -ForegroundColor Yellow
    $answer = Read-Host "Continue? (y/N)"
    if ($answer -eq 'y' -or $answer -eq 'Y') {
        & docker system prune -f
    } else {
        Write-Host "Skipped the system prune." -ForegroundColor Cyan
    }
}

Write-Host "Done." -ForegroundColor Green
```

- [ ] **Step 6: Add a repo-root `.dockerignore`**

The redrive image is built with the repository root as its context. Without this, `bin/`, `obj/` and `publish/` are uploaded to the daemon on every build.

```gitignore
**/bin/
**/obj/
publish/
.git/
.planning/
docs/
integration-infrastructure/
*.user
```

- [ ] **Step 7: Verify Floci and the seed**

Run:
```powershell
cd integration-infrastructure
./start.ps1
```
Expected: `floci-init` reaches `exited:0` and its log lists 18 queue URLs (15 queues + 3 DLQs).

Then confirm the redrive policy survived — this is the riskiest assumption in the whole design:
```powershell
docker compose run --rm --entrypoint /bin/sh floci-init -c `
  "AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_DEFAULT_REGION=us-east-1 aws --endpoint-url http://floci:4566 sqs get-queue-attributes --queue-url http://floci:4566/000000000000/it-failing --attribute-names All"
```
Expected: the returned attributes include `RedrivePolicy` with a `deadLetterTargetArn` and `VisibilityTimeout` of `5`.

**If `RedrivePolicy` is absent or DLQ redrive is unsupported by Floci, stop and report it.** Tests 16 and 28 depend on it and the spec says to cut them in that case.

- [ ] **Step 8: Commit**

```bash
git add integration-infrastructure/ .dockerignore
git commit -m "test(integration): compose skeleton with Floci and queue seeding"
```

---

### Task 2: The sink service — HTTP recording and behaviour

**Files:**
- Create: `integration-infrastructure/sink/Dockerfile`
- Create: `integration-infrastructure/sink/requirements.txt`
- Create: `integration-infrastructure/sink/app.py`
- Create: `integration-infrastructure/sink/certs/generate.sh`
- Modify: `integration-infrastructure/docker-compose.yml`

**Interfaces:**
- Consumes: nothing from Task 1 at runtime.
- Produces: `http://localhost:8080` and `https://localhost:8443` serving `GET /health`, `GET /recorded/{correlation_id}`, `DELETE /recorded/{correlation_id}`, and catch-all recording routes under `/http/*` and `/secure/*`. Record JSON shape:
  ```json
  { "transport": "http", "method": "POST", "path": "/http/post",
    "query": {"k": "v"}, "headers": {"lowercased": "value"},
    "body": "raw string", "topic": null, "at": "2026-08-30T10:00:00.000Z" }
  ```

- [ ] **Step 1: Write `sink/requirements.txt`**

```
fastapi==0.115.6
uvicorn==0.34.0
confluent-kafka==2.6.1
```

- [ ] **Step 2: Write `sink/certs/generate.sh`**

```sh
#!/bin/sh
set -e
mkdir -p /app/certs
openssl req -x509 -newkey rsa:2048 -nodes \
  -keyout /app/certs/sink.key \
  -out /app/certs/sink.crt \
  -days 3650 \
  -subj "/CN=sink" \
  -addext "subjectAltName=DNS:sink,DNS:localhost,IP:127.0.0.1"
echo "Generated self-signed certificate for CN=sink"
```

- [ ] **Step 3: Write `sink/app.py`**

One process, two uvicorn servers (8080 plain, 8443 TLS), one shared in-memory store. Kafka is wired in Task 3; the import and thread start are already present but guarded by an env var so this task is runnable on its own.

```python
"""Recording sink for the AWSRedrive integration test suite.

Records every request redrive delivers - over HTTP and (task 3) off Kafka -
and serves them back to tests keyed by a correlation id.
"""
import asyncio
import base64
import json
import os
import threading
from collections import defaultdict
from datetime import datetime, timezone

import uvicorn
from fastapi import APIRouter, FastAPI, Request, Response

# --------------------------------------------------------------------------
# Configuration
# --------------------------------------------------------------------------
HTTP_PORT = int(os.environ.get("SINK_HTTP_PORT", "8080"))
HTTPS_PORT = int(os.environ.get("SINK_HTTPS_PORT", "8443"))
CERT_FILE = os.environ.get("SINK_CERT_FILE", "/app/certs/sink.crt")
KEY_FILE = os.environ.get("SINK_KEY_FILE", "/app/certs/sink.key")

EXPECTED_AUTH_TOKEN = os.environ.get("EXPECTED_AUTH_TOKEN", "")
EXPECTED_API_KEY = os.environ.get("EXPECTED_API_KEY", "")
EXPECTED_BASIC_USER = os.environ.get("EXPECTED_BASIC_USER", "")
EXPECTED_BASIC_PASSWORD = os.environ.get("EXPECTED_BASIC_PASSWORD", "")

# --------------------------------------------------------------------------
# Store
# --------------------------------------------------------------------------
_lock = threading.Lock()
_by_correlation = defaultdict(list)
_all_records = []


def _now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def record(transport, method, path, query, headers, body, topic=None):
    """Store one delivery. Correlation id decides which bucket it lands in."""
    entry = {
        "transport": transport,
        "method": method,
        "path": path,
        "query": query or {},
        "headers": headers or {},
        "body": body,
        "topic": topic,
        "at": _now(),
    }
    cid = extract_correlation(headers, body, query)
    entry["correlation"] = cid
    with _lock:
        _all_records.append(entry)
        if cid:
            _by_correlation[cid].append(entry)
    return entry


def extract_correlation(headers, body, query):
    """Header, then JSON body field, then query parameter.

    The query case is what covers UseGET, where redrive unwraps the JSON body
    into query parameters and sends no body at all.
    """
    if headers:
        for key, value in headers.items():
            if key.lower() == "x-correlation-id" and value:
                return value
    if body:
        try:
            parsed = json.loads(body)
            if isinstance(parsed, dict):
                value = parsed.get("correlation")
                if isinstance(value, str) and value:
                    return value
        except (ValueError, TypeError):
            pass
    if query:
        value = query.get("correlation")
        if value:
            return value
    return None


# --------------------------------------------------------------------------
# Routes
# --------------------------------------------------------------------------
router = APIRouter()


@router.get("/health")
async def health():
    with _lock:
        total = len(_all_records)
    return {"status": "ok", "recorded": total}


@router.get("/recorded/{correlation_id}")
async def recorded(correlation_id: str):
    with _lock:
        return list(_by_correlation.get(correlation_id, []))


@router.delete("/recorded/{correlation_id}")
async def clear_recorded(correlation_id: str):
    with _lock:
        removed = len(_by_correlation.pop(correlation_id, []))
    return {"removed": removed}


@router.get("/recorded")
async def recorded_all(limit: int = 100):
    """Debugging aid for humans poking at the stack; tests do not use it."""
    with _lock:
        return list(_all_records[-limit:])


def _authorized(path, headers):
    """Return None when allowed, or a WWW-Authenticate value when rejected."""
    if path.startswith("/secure/token"):
        if headers.get("authorization") != EXPECTED_AUTH_TOKEN:
            return "Bearer"
        return None
    if path.startswith("/secure/apikey"):
        if headers.get("x-api-key") != EXPECTED_API_KEY:
            return "ApiKey"
        return None
    if path.startswith("/secure/basic"):
        expected = base64.b64encode(
            f"{EXPECTED_BASIC_USER}:{EXPECTED_BASIC_PASSWORD}".encode()
        ).decode()
        if headers.get("authorization") != f"Basic {expected}":
            return 'Basic realm="integration"'
        return None
    return None


@router.api_route(
    "/{prefix:path}",
    methods=["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"],
)
async def catch_all(prefix: str, request: Request):
    path = "/" + prefix
    if not (path.startswith("/http/") or path.startswith("/secure/")):
        return Response(status_code=404)

    raw = await request.body()
    body = raw.decode("utf-8", errors="replace") if raw else ""
    headers = {k.lower(): v for k, v in request.headers.items()}
    query = dict(request.query_params)

    # Record before deciding the outcome, so retry tests can count attempts.
    record("http", request.method, path, query, headers, body)

    delay_ms = query.get("delay")
    if delay_ms:
        await asyncio.sleep(int(delay_ms) / 1000.0)

    challenge = _authorized(path, headers)
    if challenge:
        return Response(status_code=401, headers={"WWW-Authenticate": challenge})

    status = query.get("status")
    if status:
        return Response(status_code=int(status), content=f"forced {status}")

    return Response(status_code=200, content="ok")


# --------------------------------------------------------------------------
# Application
# --------------------------------------------------------------------------
def build_app():
    app = FastAPI(title="AWSRedrive integration sink", docs_url=None, redoc_url=None)
    app.include_router(router)
    return app


async def main():
    app = build_app()

    plain = uvicorn.Server(
        uvicorn.Config(app, host="0.0.0.0", port=HTTP_PORT, log_level="info")
    )
    secure = uvicorn.Server(
        uvicorn.Config(
            app,
            host="0.0.0.0",
            port=HTTPS_PORT,
            log_level="info",
            ssl_certfile=CERT_FILE,
            ssl_keyfile=KEY_FILE,
        )
    )

    print(f"sink listening on http://0.0.0.0:{HTTP_PORT} and https://0.0.0.0:{HTTPS_PORT}")
    await asyncio.gather(plain.serve(), secure.serve())


if __name__ == "__main__":
    asyncio.run(main())
```

- [ ] **Step 4: Write `sink/Dockerfile`**

Build context is `integration-infrastructure/`, not `sink/`, because Task 5 copies the redrive config baseline in from `redrive/`.

```dockerfile
FROM python:3.12-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends openssl curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY sink/requirements.txt ./requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

COPY sink/certs/generate.sh ./certs/generate.sh
RUN sh ./certs/generate.sh

COPY sink/app.py ./app.py

EXPOSE 8080 8443
CMD ["python", "-u", "app.py"]
```

- [ ] **Step 5: Add the sink to `docker-compose.yml`**

```yaml
  sink:
    build:
      context: .
      dockerfile: sink/Dockerfile
    container_name: awsredrive-it-sink
    ports:
      - "${SINK_HTTP_PORT}:8080"
      - "${SINK_HTTPS_PORT}:8443"
    environment:
      SINK_HTTP_PORT: 8080
      SINK_HTTPS_PORT: 8443
      EXPECTED_AUTH_TOKEN: ${EXPECTED_AUTH_TOKEN}
      EXPECTED_API_KEY: ${EXPECTED_API_KEY}
      EXPECTED_BASIC_USER: ${EXPECTED_BASIC_USER}
      EXPECTED_BASIC_PASSWORD: ${EXPECTED_BASIC_PASSWORD}
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health"]
      interval: 3s
      timeout: 3s
      retries: 20
```

- [ ] **Step 6: Add a sink readiness probe to `start.ps1`**

Insert after the `floci-init` wait:

```powershell
$sinkPort = if ($env:SINK_HTTP_PORT) { $env:SINK_HTTP_PORT } else { 8080 }
if (-not (Wait-ForHttp -Name 'sink' -Url "http://localhost:$sinkPort/health" -Timeout $TimeoutSeconds)) {
    docker compose logs sink
    Write-Error "The sink service did not become healthy."
}
```

And extend the port table printed at the end:

```powershell
Write-Host "  Sink (HTTP)        http://localhost:$sinkPort"
Write-Host "  Sink (HTTPS)       https://localhost:$($env:SINK_HTTPS_PORT ?? 8443)"
```

- [ ] **Step 7: Verify the sink**

Run:
```powershell
cd integration-infrastructure
./start.ps1
curl.exe -s -X POST http://localhost:8080/http/post -H "content-type: application/json" -d '{"correlation":"abc123","hello":"world"}'
curl.exe -s http://localhost:8080/recorded/abc123
```
Expected: the second call returns one record with `"method": "POST"`, `"path": "/http/post"`, and the body verbatim.

Then check the negative paths:
```powershell
curl.exe -s -o NUL -w "%{http_code}\n" "http://localhost:8080/http/fail?status=500&correlation=x1"
curl.exe -s -o NUL -w "%{http_code}\n" http://localhost:8080/secure/token
curl.exe -sk -o NUL -w "%{http_code}\n" https://localhost:8443/http/tls
```
Expected: `500`, `401`, `200`.

- [ ] **Step 8: Commit**

```bash
git add integration-infrastructure/sink integration-infrastructure/docker-compose.yml integration-infrastructure/start.ps1
git commit -m "test(integration): recording sink service with TLS and auth challenges"
```

---

### Task 3: Kafka and the sink's Kafka consumer

**Files:**
- Create: `integration-infrastructure/kafka/seed.sh`
- Modify: `integration-infrastructure/sink/app.py`
- Modify: `integration-infrastructure/docker-compose.yml`
- Modify: `integration-infrastructure/start.ps1`

**Interfaces:**
- Consumes: the `record()` function and store from Task 2.
- Produces: records with `"transport": "kafka"` and a populated `"topic"`, retrievable through the same `GET /recorded/{correlation_id}` route.

- [ ] **Step 1: Write `kafka/seed.sh`**

```sh
#!/bin/sh
set -e

BOOTSTRAP="kafka:9092"
TOPICS="it-kafka-plain it-kafka-compressed"

echo "Waiting for Kafka at $BOOTSTRAP ..."
i=0
until /opt/kafka/bin/kafka-topics.sh --bootstrap-server "$BOOTSTRAP" --list >/dev/null 2>&1; do
  i=$((i + 1))
  if [ "$i" -gt 120 ]; then
    echo "Kafka did not become ready within 120s" >&2
    exit 1
  fi
  sleep 1
done
echo "Kafka is ready."

for t in $TOPICS; do
  echo "  topic $t"
  /opt/kafka/bin/kafka-topics.sh --bootstrap-server "$BOOTSTRAP" \
    --create --if-not-exists --topic "$t" --partitions 1 --replication-factor 1
done

echo "Seed complete:"
/opt/kafka/bin/kafka-topics.sh --bootstrap-server "$BOOTSTRAP" --list
```

- [ ] **Step 2: Add the Kafka consumer to `sink/app.py`**

Insert the config constants next to the existing ones:

```python
KAFKA_BOOTSTRAP = os.environ.get("KAFKA_BOOTSTRAP", "")
KAFKA_TOPICS = [t for t in os.environ.get("KAFKA_TOPICS", "").split(",") if t]
```

Add this block above `build_app()`:

```python
# --------------------------------------------------------------------------
# Kafka consumer
# --------------------------------------------------------------------------
def consume_kafka():
    """Record everything redrive produces to the test topics.

    Runs on a daemon thread. Failures are logged and retried rather than
    raised - the HTTP half of the sink must keep working regardless.
    """
    from confluent_kafka import Consumer, KafkaError

    consumer = Consumer(
        {
            "bootstrap.servers": KAFKA_BOOTSTRAP,
            "group.id": "integration-sink",
            "auto.offset.reset": "earliest",
            "enable.auto.commit": True,
        }
    )
    consumer.subscribe(KAFKA_TOPICS)
    print(f"kafka consumer subscribed to {KAFKA_TOPICS} via {KAFKA_BOOTSTRAP}")

    while True:
        try:
            msg = consumer.poll(1.0)
            if msg is None:
                continue
            if msg.error():
                if msg.error().code() != KafkaError._PARTITION_EOF:
                    print(f"kafka consumer error: {msg.error()}")
                continue

            raw = msg.value()
            body = raw.decode("utf-8", errors="replace") if raw else ""
            headers = {k: _decode(v) for k, v in (msg.headers() or [])}
            record("kafka", None, None, None, headers, body, topic=msg.topic())
        except Exception as exc:  # keep the thread alive
            print(f"kafka consumer exception: {exc}")


def _decode(value):
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    return value


def start_kafka_consumer():
    if not KAFKA_BOOTSTRAP or not KAFKA_TOPICS:
        print("kafka consumer disabled (KAFKA_BOOTSTRAP/KAFKA_TOPICS unset)")
        return
    thread = threading.Thread(target=consume_kafka, name="kafka-consumer", daemon=True)
    thread.start()
```

Call it at the top of `main()`:

```python
async def main():
    start_kafka_consumer()
    app = build_app()
```

- [ ] **Step 3: Add Kafka services to `docker-compose.yml`**

```yaml
  kafka:
    image: ${KAFKA_IMAGE}
    container_name: awsredrive-it-kafka
    ports:
      - "${KAFKA_PORT}:29092"
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: broker,controller
      KAFKA_LISTENERS: PLAINTEXT://:9092,CONTROLLER://:9093,EXTERNAL://:29092
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092,EXTERNAL://localhost:29092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,EXTERNAL:PLAINTEXT
      KAFKA_CONTROLLER_LISTENER_NAMES: CONTROLLER
      KAFKA_CONTROLLER_QUORUM_VOTERS: 1@kafka:9093
      KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: 1
      KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS: 0
      KAFKA_AUTO_CREATE_TOPICS_ENABLE: "true"
      CLUSTER_ID: 5L6g3nShT-eMCtK--X86sw

  kafka-init:
    image: ${KAFKA_IMAGE}
    container_name: awsredrive-it-kafka-init
    depends_on:
      - kafka
    entrypoint: ["/bin/sh", "/seed/seed.sh"]
    volumes:
      - ./kafka/seed.sh:/seed/seed.sh:ro
```

Add to the `sink` service:

```yaml
    depends_on:
      kafka-init:
        condition: service_completed_successfully
    environment:
      KAFKA_BOOTSTRAP: kafka:9092
      KAFKA_TOPICS: it-kafka-plain,it-kafka-compressed
```

(keep the existing `environment` entries — these are additions to the same map)

- [ ] **Step 4: Add the Kafka init wait to `start.ps1`**

Insert before the sink probe:

```powershell
if (-not (Wait-ForInitContainer -Service 'kafka-init' -Timeout $TimeoutSeconds)) {
    docker compose logs kafka
    Write-Error "Kafka topic creation did not complete."
}
```

- [ ] **Step 5: Verify the Kafka path**

Run:
```powershell
cd integration-infrastructure
./stop.ps1
./start.ps1
docker compose exec kafka /bin/sh -c "echo '{\"correlation\":\"kafka-smoke\",\"v\":1}' | /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server kafka:9092 --topic it-kafka-plain"
curl.exe -s http://localhost:8080/recorded/kafka-smoke
```
Expected: one record with `"transport": "kafka"` and `"topic": "it-kafka-plain"`.

- [ ] **Step 6: Commit**

```bash
git add integration-infrastructure/
git commit -m "test(integration): Kafka broker, topic seeding and sink consumer"
```

---

### Task 4: Redrive under test, with all 16 aliases

**Files:**
- Create: `integration-infrastructure/redrive/config.json`
- Create: `integration-infrastructure/redrive/appsettings.json`
- Create: `integration-infrastructure/redrive/NLog.config`
- Modify: `integration-infrastructure/docker-compose.yml`
- Modify: `integration-infrastructure/start.ps1`

**Interfaces:**
- Consumes: queues from Task 1, sink from Task 2, Kafka from Task 3.
- Produces: redrive running with 15 active aliases (`it-inactive` inactive, `it-reload` absent), dashboard on `http://localhost:5000`.

- [ ] **Step 1: Write `redrive/appsettings.json`**

Metrics interval is dropped to 5s so `DashboardTests` does not wait a minute.

```json
{
  "DefaultLogLevel": "Debug",
  "Dashboard": {
    "Enabled": true,
    "Port": 5000,
    "RefreshIntervalMs": 1000
  },
  "Metrics": {
    "Enabled": true,
    "IntervalSeconds": 5
  }
}
```

- [ ] **Step 2: Write `redrive/NLog.config`**

Console only — logs are read with `docker compose logs`, and a chiseled image has nowhere useful to put a file.

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
      autoReload="true"
      throwExceptions="false">
  <targets>
    <target name="logconsole" xsi:type="Console">
      <layout xsi:type="JsonLayout" includeEventProperties="true" excludeEmptyProperties="true">
        <attribute name="@timestamp" layout="${date:universalTime=true:format=yyyy-MM-ddTHH\:mm\:ss.fffZ}" />
        <attribute name="level" layout="${level:upperCase=true}" />
        <attribute name="logger" layout="${logger}" />
        <attribute name="message" layout="${message}" />
        <attribute name="exception" layout="${exception:format=tostring}" />
      </layout>
    </target>
  </targets>
  <rules>
    <logger name="Metrics" minlevel="Info" writeTo="logconsole" final="true" />
    <logger name="Microsoft.*" maxlevel="Info" final="true" />
    <logger name="Microsoft.*" minlevel="Warn" writeTo="logconsole" final="true" />
    <logger name="*" minlevel="Trace" writeTo="logconsole" />
  </rules>
</nlog>
```

- [ ] **Step 3: Write `redrive/config.json`**

Every entry carries `ServiceUrl` so `AwsQueueClient.Init` uses Floci rather than a real region endpoint, and an explicit `QueueUrl` on the `floci` hostname — the URL the *container* must use, which is not the one tests use.

```json
[
  {
    "Alias": "it-http-post",
    "QueueUrl": "http://floci:4566/000000000000/it-http-post",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/post",
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-http-put",
    "QueueUrl": "http://floci:4566/000000000000/it-http-put",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/put",
    "UsePUT": true,
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-http-delete",
    "QueueUrl": "http://floci:4566/000000000000/it-http-delete",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/delete",
    "UseDelete": true,
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-http-get",
    "QueueUrl": "http://floci:4566/000000000000/it-http-get",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/get",
    "UseGET": true,
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-auth-token",
    "QueueUrl": "http://floci:4566/000000000000/it-auth-token",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/secure/token",
    "AuthToken": "Bearer it-integration-token",
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-gateway-token",
    "QueueUrl": "http://floci:4566/000000000000/it-gateway-token",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/secure/apikey",
    "AwsGatewayToken": "it-integration-api-key",
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-basic-auth",
    "QueueUrl": "http://floci:4566/000000000000/it-basic-auth",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/secure/basic",
    "BasicAuthUserName": "it-user",
    "BasicAuthPassword": "it-password",
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-sns-unpack",
    "QueueUrl": "http://floci:4566/000000000000/it-sns-unpack",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/sns",
    "UnpackAttributesAsHeaders": true,
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-timeout",
    "QueueUrl": "http://floci:4566/000000000000/it-timeout",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/slow?delay=6000",
    "Timeout": 2000,
    "Active": true,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-failing",
    "QueueUrl": "http://floci:4566/000000000000/it-failing",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/fail?status=500",
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-https-lax",
    "QueueUrl": "http://floci:4566/000000000000/it-https-lax",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "https://sink:8443/http/tls",
    "IgnoreCertificateErrors": true,
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-https-strict",
    "QueueUrl": "http://floci:4566/000000000000/it-https-strict",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "https://sink:8443/http/tls-strict",
    "IgnoreCertificateErrors": false,
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-inactive",
    "QueueUrl": "http://floci:4566/000000000000/it-inactive",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveUrl": "http://sink:8080/http/inactive",
    "Active": false,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-kafka-plain",
    "QueueUrl": "http://floci:4566/000000000000/it-kafka-plain",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveKafkaTopic": "it-kafka-plain",
    "KafkaBootstrapServers": "kafka:9092",
    "KafkaClientId": "it-redrive",
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  },
  {
    "Alias": "it-kafka-compressed",
    "QueueUrl": "http://floci:4566/000000000000/it-kafka-compressed",
    "ServiceUrl": "http://floci:4566",
    "Region": "us-east-1",
    "AccessKey": "test",
    "SecretKey": "test",
    "RedriveKafkaTopic": "it-kafka-compressed",
    "KafkaBootstrapServers": "kafka:9092",
    "KafkaClientId": "it-redrive",
    "UseKafkaCompression": true,
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Debug"
  }
]
```

- [ ] **Step 4: Add the redrive service to `docker-compose.yml`**

`config.json` is mounted read-write and **without** `:ro` — Task 5's admin endpoint rewrites it through the sink's mount of the same host file.

```yaml
  redrive:
    build:
      context: ..
      dockerfile: Dockerfile.image
      target: console-image
    container_name: awsredrive-it-redrive
    depends_on:
      floci-init:
        condition: service_completed_successfully
      kafka-init:
        condition: service_completed_successfully
      sink:
        condition: service_healthy
    ports:
      - "${REDRIVE_DASHBOARD_PORT}:5000"
    environment:
      AWS_REGION: ${AWS_REGION}
      AWS_DEFAULT_REGION: ${AWS_REGION}
      AWS_ACCESS_KEY_ID: test
      AWS_SECRET_ACCESS_KEY: test
    volumes:
      - ./redrive/config.json:/app/config.json
      - ./redrive/appsettings.json:/app/appsettings.json:ro
      - ./redrive/NLog.config:/app/NLog.config:ro
```

- [ ] **Step 5: Add the redrive readiness probe to `start.ps1`**

```powershell
$dashPort = if ($env:REDRIVE_DASHBOARD_PORT) { $env:REDRIVE_DASHBOARD_PORT } else { 5000 }
if (-not (Wait-ForHttp -Name 'redrive' -Url "http://localhost:$dashPort/health" -Timeout $TimeoutSeconds)) {
    docker compose logs redrive
    Write-Error "Redrive did not become healthy."
}
```

And extend the port table:

```powershell
Write-Host "  Kafka (external)   localhost:$($env:KAFKA_PORT ?? 29092)"
Write-Host "  Redrive dashboard  http://localhost:$dashPort"
```

- [ ] **Step 6: Verify the whole path end to end by hand**

Run:
```powershell
cd integration-infrastructure
./stop.ps1
./start.ps1
```

Then push a message through and watch it land:
```powershell
$env:AWS_ACCESS_KEY_ID='test'; $env:AWS_SECRET_ACCESS_KEY='test'; $env:AWS_DEFAULT_REGION='us-east-1'
docker compose run --rm --entrypoint /bin/sh floci-init -c `
  "AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_DEFAULT_REGION=us-east-1 aws --endpoint-url http://floci:4566 sqs send-message --queue-url http://floci:4566/000000000000/it-http-post --message-body '{\"correlation\":\"e2e-smoke\",\"hello\":\"world\"}'"
Start-Sleep -Seconds 5
curl.exe -s http://localhost:8080/recorded/e2e-smoke
```
Expected: one record, `"method": "POST"`, `"path": "/http/post"`, body containing `e2e-smoke`.

Also confirm the dashboard sees the alias:
```powershell
curl.exe -s http://localhost:5000/api/status
```
Expected: JSON listing all 15 active aliases, with `it-http-post` showing `messagesReceived` of at least 1.

**If `/api/status` reports queue receive errors for every alias**, the region/signing risk from the spec has materialised — check `docker compose logs redrive` and confirm the `AWS_REGION` environment variables took effect.

- [ ] **Step 7: Commit**

```bash
git add integration-infrastructure/
git commit -m "test(integration): redrive service under test with all 16 aliases"
```

---

### Task 5: Sink admin endpoint for runtime config changes

**Files:**
- Modify: `integration-infrastructure/sink/app.py`
- Modify: `integration-infrastructure/sink/Dockerfile`
- Modify: `integration-infrastructure/docker-compose.yml`

**Interfaces:**
- Consumes: `redrive/config.json`, bind-mounted read-write into the sink at `/redrive-config/config.json`.
- Produces: `GET /admin/redrive-config` (returns the alias array), `POST /admin/redrive-config` (replaces it, body is the array), `DELETE /admin/redrive-config` (restores the baseline baked into the image).

- [ ] **Step 1: Add the admin routes to `sink/app.py`**

Add the constants next to the others:

```python
REDRIVE_CONFIG_PATH = os.environ.get("REDRIVE_CONFIG_PATH", "/redrive-config/config.json")
REDRIVE_CONFIG_BASELINE = os.environ.get("REDRIVE_CONFIG_BASELINE", "/app/config.baseline.json")
```

Add the routes to `router`:

```python
def _write_config_in_place(entries):
    """Rewrite config.json without replacing the inode.

    A rename-based atomic write would break the bind mount into the redrive
    container - it would keep reading the old inode forever. Truncate and
    write is deliberate here.
    """
    payload = json.dumps(entries, indent=2)
    with open(REDRIVE_CONFIG_PATH, "w", encoding="utf-8") as handle:
        handle.write(payload)
        handle.flush()
        os.fsync(handle.fileno())


@router.get("/admin/redrive-config")
async def get_redrive_config():
    with open(REDRIVE_CONFIG_PATH, "r", encoding="utf-8") as handle:
        return json.load(handle)


@router.post("/admin/redrive-config")
async def set_redrive_config(request: Request):
    entries = await request.json()
    if not isinstance(entries, list):
        return Response(status_code=400, content="expected a JSON array of aliases")
    _write_config_in_place(entries)
    return {"aliases": len(entries)}


@router.delete("/admin/redrive-config")
async def reset_redrive_config():
    with open(REDRIVE_CONFIG_BASELINE, "r", encoding="utf-8") as handle:
        entries = json.load(handle)
    _write_config_in_place(entries)
    return {"aliases": len(entries), "restored": True}
```

The catch-all route already 404s anything outside `/http/` and `/secure/`, and FastAPI matches the more specific `/admin/...` routes first because they are registered earlier — keep the admin routes above `catch_all` in the file.

- [ ] **Step 2: Bake the baseline into the sink image**

Add to `sink/Dockerfile`, before the `EXPOSE` line:

```dockerfile
COPY redrive/config.json ./config.baseline.json
```

- [ ] **Step 3: Mount the live config into the sink**

Add to the `sink` service in `docker-compose.yml`:

```yaml
    volumes:
      - ./redrive/config.json:/redrive-config/config.json
```

- [ ] **Step 4: Verify the admin endpoint round-trips**

Run:
```powershell
cd integration-infrastructure
./stop.ps1
./start.ps1
curl.exe -s http://localhost:8080/admin/redrive-config | python -c "import json,sys; print(len(json.load(sys.stdin)))"
curl.exe -s -X DELETE http://localhost:8080/admin/redrive-config
```
Expected: `16`, then `{"aliases":16,"restored":true}`.

Confirm the bind mount still points at the same file after a write:
```powershell
docker compose exec redrive /bin/sh -c "wc -c /app/config.json" 2>$null
Get-Content ./redrive/config.json -TotalCount 3
```
Expected: the host file reflects the write. (The `exec` will fail — the redrive image is chiseled and has no shell. That is expected; the host-side check is the real one.)

- [ ] **Step 5: Commit**

```bash
git add integration-infrastructure/
git commit -m "test(integration): sink admin endpoint for runtime config changes"
```

---

### Task 6: Test project scaffolding

**Files:**
- Delete: `Tests/AWSRedrive.Test.Integration/IntegrationTests.cs`
- Modify: `Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj`
- Create: `Tests/AWSRedrive.Test.Integration/integrationsettings.json`
- Create: `Tests/AWSRedrive.Test.Integration/Infrastructure/TestEnvironment.cs`
- Create: `Tests/AWSRedrive.Test.Integration/Infrastructure/RecordedRequest.cs`
- Create: `Tests/AWSRedrive.Test.Integration/Infrastructure/SinkClient.cs`
- Create: `Tests/AWSRedrive.Test.Integration/Infrastructure/TestQueueClient.cs`
- Create: `Tests/AWSRedrive.Test.Integration/Infrastructure/StackPreflight.cs`

**Interfaces:**
- Consumes: the running stack from Tasks 1-5.
- Produces, for every later task:
  - `TestEnvironment.Instance` with `SinkHttpUrl`, `SinkHttpsUrl`, `DashboardUrl`, `FlociUrl`, `AccountId`, `CreateSqsClient()`, `QueueUrl(string name)`
  - `SinkClient.WaitForDeliveryAsync(string correlationId, TimeSpan? timeout = null, int minimumCount = 1)` → `IReadOnlyList<RecordedRequest>`
  - `SinkClient.GetRecordedAsync(string correlationId)` → `IReadOnlyList<RecordedRequest>`
  - `SinkClient.AssertNoDeliveryAsync(string correlationId, TimeSpan window)`
  - `SinkClient.GetConfigAsync()`, `SetConfigAsync(JsonArray)`, `ResetConfigAsync()`
  - `TestQueueClient.SendAsync(string queueName, string body, IDictionary<string,string>? attributes = null)`
  - `TestQueueClient.ApproximateDepthAsync(string queueName)`
  - `TestQueueClient.WaitForDlqMessageAsync(string dlqName, string correlationId, TimeSpan timeout)`
  - `RecordedRequest` with `Transport`, `Method`, `Path`, `Query`, `Headers`, `Body`, `Topic`, `At`, `Correlation`, and `Header(string name)`
  - `[Collection("integration")]` bound to `StackPreflight`

- [ ] **Step 1: Delete the placeholder tests**

```bash
git rm Tests/AWSRedrive.Test.Integration/IntegrationTests.cs
```

Both tests only asserted that a request to a nonexistent host throws.

- [ ] **Step 2: Rewrite the csproj**

`Microsoft.NET.Test.Sdk` is absent today, which is why this project has never run under a test runner. No Docker packages.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <SonarQubeTestProject>true</SonarQubeTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AWSSDK.SQS" Version="4.0.3.7" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Projects\AWSRedrive\AWSRedrive.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="integrationsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write `integrationsettings.json`**

```json
{
  "SinkHttpUrl": "http://localhost:8080",
  "SinkHttpsUrl": "https://localhost:8443",
  "DashboardUrl": "http://localhost:5000",
  "FlociUrl": "http://localhost:4566",
  "AccountId": "000000000000",
  "Region": "us-east-1",
  "AccessKey": "test",
  "SecretKey": "test",
  "ExpectedAuthToken": "Bearer it-integration-token",
  "ExpectedApiKey": "it-integration-api-key",
  "ExpectedBasicUser": "it-user",
  "ExpectedBasicPassword": "it-password"
}
```

- [ ] **Step 4: Write `Infrastructure/TestEnvironment.cs`**

```csharp
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// Endpoints and credentials for the stack started by
/// integration-infrastructure/start.ps1. Every value can be overridden with an
/// environment variable of the form AWSREDRIVE_IT_&lt;PROPERTY&gt;.
/// </summary>
public sealed class TestEnvironment
{
    private const string EnvPrefix = "AWSREDRIVE_IT_";

    public static TestEnvironment Instance { get; } = Load();

    public string SinkHttpUrl { get; init; } = "http://localhost:8080";
    public string SinkHttpsUrl { get; init; } = "https://localhost:8443";
    public string DashboardUrl { get; init; } = "http://localhost:5000";
    public string FlociUrl { get; init; } = "http://localhost:4566";
    public string AccountId { get; init; } = "000000000000";
    public string Region { get; init; } = "us-east-1";
    public string AccessKey { get; init; } = "test";
    public string SecretKey { get; init; } = "test";
    public string ExpectedAuthToken { get; init; } = "Bearer it-integration-token";
    public string ExpectedApiKey { get; init; } = "it-integration-api-key";
    public string ExpectedBasicUser { get; init; } = "it-user";
    public string ExpectedBasicPassword { get; init; } = "it-password";

    private static TestEnvironment Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "integrationsettings.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<TestEnvironment>(File.ReadAllText(path),
                  new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
              ?? new TestEnvironment()
            : new TestEnvironment();

        return new TestEnvironment
        {
            SinkHttpUrl = Override(nameof(SinkHttpUrl), settings.SinkHttpUrl),
            SinkHttpsUrl = Override(nameof(SinkHttpsUrl), settings.SinkHttpsUrl),
            DashboardUrl = Override(nameof(DashboardUrl), settings.DashboardUrl),
            FlociUrl = Override(nameof(FlociUrl), settings.FlociUrl),
            AccountId = Override(nameof(AccountId), settings.AccountId),
            Region = Override(nameof(Region), settings.Region),
            AccessKey = Override(nameof(AccessKey), settings.AccessKey),
            SecretKey = Override(nameof(SecretKey), settings.SecretKey),
            ExpectedAuthToken = Override(nameof(ExpectedAuthToken), settings.ExpectedAuthToken),
            ExpectedApiKey = Override(nameof(ExpectedApiKey), settings.ExpectedApiKey),
            ExpectedBasicUser = Override(nameof(ExpectedBasicUser), settings.ExpectedBasicUser),
            ExpectedBasicPassword = Override(nameof(ExpectedBasicPassword), settings.ExpectedBasicPassword)
        };
    }

    private static string Override(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(EnvPrefix + name.ToUpperInvariant());
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>
    /// The queue URL as seen from the test process. Redrive uses the same path
    /// on the "floci" hostname instead; both address the same queue.
    /// </summary>
    public string QueueUrl(string queueName) => $"{FlociUrl}/{AccountId}/{queueName}";

    public IAmazonSQS CreateSqsClient()
    {
        var config = new AmazonSQSConfig
        {
            ServiceURL = FlociUrl,
            AuthenticationRegion = Region
        };
        return new AmazonSQSClient(new BasicAWSCredentials(AccessKey, SecretKey), config);
    }
}
```

- [ ] **Step 5: Write `Infrastructure/RecordedRequest.cs`**

```csharp
using System.Text.Json.Serialization;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>One delivery observed by the sink, over HTTP or off a Kafka topic.</summary>
public sealed class RecordedRequest
{
    [JsonPropertyName("transport")] public string Transport { get; set; } = "";
    [JsonPropertyName("method")] public string? Method { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("query")] public Dictionary<string, string> Query { get; set; } = new();
    [JsonPropertyName("headers")] public Dictionary<string, string> Headers { get; set; } = new();
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("topic")] public string? Topic { get; set; }
    [JsonPropertyName("at")] public string? At { get; set; }
    [JsonPropertyName("correlation")] public string? Correlation { get; set; }

    /// <summary>Header lookup by name, case-insensitive. Null when absent.</summary>
    public string? Header(string name)
    {
        foreach (var pair in Headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }
        return null;
    }

    public bool HasHeader(string name) => Header(name) is not null;
}
```

- [ ] **Step 6: Write `Infrastructure/SinkClient.cs`**

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>Reads back what the sink recorded, and drives its admin endpoint.</summary>
public sealed class SinkClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SinkClient(TestEnvironment? environment = null)
    {
        var env = environment ?? TestEnvironment.Instance;
        _baseUrl = env.SinkHttpUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<IReadOnlyList<RecordedRequest>> GetRecordedAsync(string correlationId)
    {
        var json = await _http.GetStringAsync($"{_baseUrl}/recorded/{correlationId}");
        return JsonSerializer.Deserialize<List<RecordedRequest>>(json) ?? new List<RecordedRequest>();
    }

    /// <summary>
    /// Polls until at least <paramref name="minimumCount"/> deliveries carrying this
    /// correlation id have been recorded. Throws with a readable message on timeout.
    /// </summary>
    public async Task<IReadOnlyList<RecordedRequest>> WaitForDeliveryAsync(
        string correlationId,
        TimeSpan? timeout = null,
        int minimumCount = 1)
    {
        var budget = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + budget;

        IReadOnlyList<RecordedRequest> records = Array.Empty<RecordedRequest>();
        while (DateTime.UtcNow < deadline)
        {
            records = await GetRecordedAsync(correlationId);
            if (records.Count >= minimumCount)
            {
                return records;
            }
            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"Expected at least {minimumCount} delivery/deliveries for correlation " +
            $"'{correlationId}' within {budget.TotalSeconds:0}s, but saw {records.Count}. " +
            "Check `docker compose logs redrive` in integration-infrastructure/.");
    }

    /// <summary>Asserts nothing arrives for this correlation id within the window.</summary>
    public async Task AssertNoDeliveryAsync(string correlationId, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var records = await GetRecordedAsync(correlationId);
            if (records.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Expected no delivery for correlation '{correlationId}', but the sink " +
                    $"recorded {records.Count}: {records[0].Method} {records[0].Path}");
            }
            await Task.Delay(PollInterval);
        }
    }

    public async Task<JsonArray> GetConfigAsync()
    {
        var json = await _http.GetStringAsync($"{_baseUrl}/admin/redrive-config");
        return JsonNode.Parse(json)!.AsArray();
    }

    public async Task SetConfigAsync(JsonArray entries)
    {
        var content = new StringContent(entries.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_baseUrl}/admin/redrive-config", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetConfigAsync()
    {
        var response = await _http.DeleteAsync($"{_baseUrl}/admin/redrive-config");
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 7: Write `Infrastructure/TestQueueClient.cs`**

```csharp
using Amazon.SQS;
using Amazon.SQS.Model;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>Sends messages into Floci and inspects queues from the test process.</summary>
public sealed class TestQueueClient : IDisposable
{
    private readonly TestEnvironment _env;
    private readonly IAmazonSQS _sqs;

    public TestQueueClient(TestEnvironment? environment = null)
    {
        _env = environment ?? TestEnvironment.Instance;
        _sqs = _env.CreateSqsClient();
    }

    public async Task SendAsync(
        string queueName,
        string body,
        IDictionary<string, string>? attributes = null)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = _env.QueueUrl(queueName),
            MessageBody = body
        };

        if (attributes is { Count: > 0 })
        {
            request.MessageAttributes = attributes.ToDictionary(
                pair => pair.Key,
                pair => new MessageAttributeValue { DataType = "String", StringValue = pair.Value });
        }

        await _sqs.SendMessageAsync(request);
    }

    public async Task<int> ApproximateDepthAsync(string queueName)
    {
        var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = _env.QueueUrl(queueName),
            AttributeNames = new List<string> { "ApproximateNumberOfMessages" }
        });

        return response.Attributes.TryGetValue("ApproximateNumberOfMessages", out var value)
            && int.TryParse(value, out var count)
                ? count
                : 0;
    }

    public async Task<string?> GetRedrivePolicyAsync(string queueName)
    {
        var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = _env.QueueUrl(queueName),
            AttributeNames = new List<string> { "RedrivePolicy" }
        });

        return response.Attributes.TryGetValue("RedrivePolicy", out var value) ? value : null;
    }

    /// <summary>
    /// Polls a dead letter queue until a message whose body contains the correlation
    /// id shows up. Received messages are left on the queue.
    /// </summary>
    public async Task<Message?> WaitForDlqMessageAsync(
        string dlqName,
        string correlationId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _env.QueueUrl(dlqName),
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 2,
                VisibilityTimeout = 1
            });

            var match = response.Messages?.FirstOrDefault(
                m => m.Body is not null && m.Body.Contains(correlationId, StringComparison.Ordinal));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public async Task PurgeAsync(string queueName)
    {
        try
        {
            await _sqs.PurgeQueueAsync(new PurgeQueueRequest { QueueUrl = _env.QueueUrl(queueName) });
        }
        catch (AmazonSQSException)
        {
            // PurgeQueue is rate limited to once every 60s per queue, and Floci may
            // not implement it at all. Neither is worth failing a test over.
        }
    }

    public void Dispose() => _sqs.Dispose();
}
```

- [ ] **Step 8: Write `Infrastructure/StackPreflight.cs`**

```csharp
using Xunit;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// Probes the stack once per run. Without this, a forgotten start.ps1 produces
/// 28 socket timeouts instead of one sentence saying what to do.
/// </summary>
public sealed class StackPreflight : IAsyncLifetime
{
    public TestEnvironment Environment { get; } = TestEnvironment.Instance;

    public async Task InitializeAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var probes = new (string Name, string Url)[]
        {
            ("sink", $"{Environment.SinkHttpUrl.TrimEnd('/')}/health"),
            ("redrive dashboard", $"{Environment.DashboardUrl.TrimEnd('/')}/health")
        };

        var unreachable = new List<string>();
        foreach (var (name, url) in probes)
        {
            try
            {
                var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    unreachable.Add($"{name} ({url} returned {(int)response.StatusCode})");
                }
            }
            catch (Exception ex)
            {
                unreachable.Add($"{name} ({url}: {ex.GetBaseException().Message})");
            }
        }

        using var queues = new TestQueueClient(Environment);
        try
        {
            await queues.ApproximateDepthAsync("it-http-post");
        }
        catch (Exception ex)
        {
            unreachable.Add($"Floci SQS ({Environment.FlociUrl}: {ex.GetBaseException().Message})");
        }

        if (unreachable.Count > 0)
        {
            throw new InvalidOperationException(
                "The integration stack is not running. Start it first:\r\n" +
                "    cd integration-infrastructure\r\n" +
                "    ./start.ps1\r\n\r\n" +
                "Unreachable: " + string.Join("; ", unreachable));
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// All integration tests join this collection so the preflight runs once and the
/// tests within it run sequentially against the shared stack.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<StackPreflight>
{
    public const string Name = "integration";
}
```

- [ ] **Step 9: Verify it builds**

Run: `dotnet build Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj -c Debug`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj --list-tests`
Expected: the runner loads the assembly and reports no tests (none exist yet).

- [ ] **Step 10: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/
git commit -m "test(integration): test project scaffolding, sink and queue clients"
```

---

### Task 7: HttpVerbTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/HttpVerbTests.cs`

**Interfaces:**
- Consumes: everything produced by Task 6.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

Six tests covering the four verbs, the non-JSON GET body, and a large Unicode payload.

```csharp
using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
public class HttpVerbTests
{
    private readonly SinkClient _sink = new();

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private static string JsonBody(string correlation, string value = "world") =>
        JsonSerializer.Serialize(new { correlation, hello = value });

    [Fact]
    public async Task PostIsTheDefaultVerbAndTheBodyArrivesVerbatim()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation);

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-post", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("POST", record.Method);
        Assert.Equal("/http/post", record.Path);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task UsePutSendsAPutWithTheBodyIntact()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation);

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-put", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("PUT", record.Method);
        Assert.Equal("/http/put", record.Path);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task UseDeleteSendsADeleteThatStillCarriesTheBody()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation);

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-delete", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("DELETE", record.Method);
        Assert.Equal("/http/delete", record.Path);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task UseGetUnwrapsTheJsonBodyIntoQueryParameters()
    {
        var correlation = NewCorrelation();
        var body = JsonSerializer.Serialize(new { correlation, hello = "world", count = "42" });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-get", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("GET", record.Method);
        Assert.Equal("/http/get", record.Path);
        Assert.Equal("world", record.Query["hello"]);
        Assert.Equal("42", record.Query["count"]);
        Assert.True(string.IsNullOrEmpty(record.Body), "A GET must not carry a body.");
    }

    [Fact]
    public async Task UseGetWithANonJsonBodyStillDeliversWithoutQueryParameters()
    {
        // The correlation id has to ride in a message attribute here: redrive can
        // only turn a *JSON* body into query parameters, and this body is not JSON.
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync(
            "it-http-get",
            "this is not json at all",
            new Dictionary<string, string> { ["X-Correlation-Id"] = correlation });

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("GET", record.Method);
        Assert.Equal("/http/get", record.Path);
        Assert.False(record.Query.ContainsKey("hello"));
    }

    [Fact]
    public async Task ALargeUnicodePayloadArrivesIntact()
    {
        var correlation = NewCorrelation();
        // ~200KB, comfortably under the 256KB SQS limit, with multi-byte characters
        // that would break any naive byte/char length handling on the way through.
        var filler = string.Concat(Enumerable.Repeat("αβγδε-日本語-🚀-", 8000));
        var body = JsonSerializer.Serialize(new { correlation, filler });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-post", body);

        var records = await _sink.WaitForDeliveryAsync(correlation, TimeSpan.FromSeconds(45));

        var record = Assert.Single(records);
        Assert.Equal(body, record.Body);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~HttpVerbTests"`
Expected: 6 passed.

If `UseGetWithANonJsonBodyStillDeliversWithoutQueryParameters` fails to find its correlation id, confirm that Floci propagates message attributes — `HttpMessageProcessor.AddAttributes` turns them into headers, and the sink reads `X-Correlation-Id` from there.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/HttpVerbTests.cs
git commit -m "test(integration): HTTP verb and payload tests"
```

---

### Task 8: AuthenticationTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/AuthenticationTests.cs`

**Interfaces:**
- Consumes: Task 6 output.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

```csharp
using System.Text;
using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
public class AuthenticationTests
{
    private readonly SinkClient _sink = new();
    private readonly TestEnvironment _env = TestEnvironment.Instance;

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private static string JsonBody(string correlation) =>
        JsonSerializer.Serialize(new { correlation });

    [Fact]
    public async Task AuthTokenArrivesAsTheAuthorizationHeader()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-auth-token", JsonBody(correlation));

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/secure/token", record.Path);
        Assert.Equal(_env.ExpectedAuthToken, record.Header("Authorization"));
    }

    [Fact]
    public async Task AwsGatewayTokenArrivesAsTheXApiKeyHeader()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-gateway-token", JsonBody(correlation));

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/secure/apikey", record.Path);
        Assert.Equal(_env.ExpectedApiKey, record.Header("x-api-key"));
    }

    [Fact]
    public async Task BasicCredentialsAreAcceptedBySinkThatChallengesEveryoneElse()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-basic-auth", JsonBody(correlation));

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/secure/basic", record.Path);

        var expected = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_env.ExpectedBasicUser}:{_env.ExpectedBasicPassword}"));
        Assert.Equal(expected, record.Header("Authorization"));

        // A single recorded delivery proves the sink did not 401 and force a retry:
        // a rejected attempt would be redelivered and recorded a second time.
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~AuthenticationTests"`
Expected: 3 passed.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/AuthenticationTests.cs
git commit -m "test(integration): authentication mode tests"
```

---

### Task 9: HeaderPropagationTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/HeaderPropagationTests.cs`

**Interfaces:**
- Consumes: Task 6 output.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

`HttpMessageProcessor._ignoredHeaders` is `["content-length", "host", "accept-encoding", "content-type", "accept"]`, and `AddAttributes` skips those names. Test 11 pins that.

```csharp
using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
public class HeaderPropagationTests
{
    private readonly SinkClient _sink = new();

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SqsMessageAttributesBecomeHttpHeaders()
    {
        var correlation = NewCorrelation();
        var body = JsonSerializer.Serialize(new { correlation });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-post", body, new Dictionary<string, string>
        {
            ["X-Trace-Id"] = "trace-12345",
            ["X-Tenant"] = "acme"
        });

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("trace-12345", record.Header("X-Trace-Id"));
        Assert.Equal("acme", record.Header("X-Tenant"));
    }

    [Fact]
    public async Task ReservedHeaderNamesSentAsAttributesAreNotPropagated()
    {
        var correlation = NewCorrelation();
        var body = JsonSerializer.Serialize(new { correlation });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-post", body, new Dictionary<string, string>
        {
            ["Host"] = "evil.example.com",
            ["Content-Type"] = "text/csv",
            ["Accept"] = "application/xml",
            ["Accept-Encoding"] = "br",
            ["Content-Length"] = "999999",
            ["X-Allowed"] = "kept"
        });

        var records = await _sink.WaitForDeliveryAsync(correlation);
        var record = Assert.Single(records);

        // The allowed one proves the attributes were processed at all.
        Assert.Equal("kept", record.Header("X-Allowed"));

        Assert.NotEqual("evil.example.com", record.Header("Host"));
        Assert.NotEqual("text/csv", record.Header("Content-Type"));
        Assert.NotEqual("application/xml", record.Header("Accept"));
        Assert.NotEqual("br", record.Header("Accept-Encoding"));
        Assert.NotEqual("999999", record.Header("Content-Length"));
    }

    [Fact]
    public async Task UnpackAttributesAsHeadersLiftsSnsMessageAttributes()
    {
        var correlation = NewCorrelation();

        // The shape AWSRedrive.Models.SnsEnvelope binds: MessageAttributes is a map
        // of name to an object with a Value property.
        var body = JsonSerializer.Serialize(new
        {
            correlation,
            Type = "Notification",
            Message = "payload",
            MessageAttributes = new Dictionary<string, object>
            {
                ["X-Sns-Trace"] = new { Type = "String", Value = "sns-trace-99" },
                ["X-Sns-Origin"] = new { Type = "String", Value = "orders" }
            }
        });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-sns-unpack", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/http/sns", record.Path);
        Assert.Equal("sns-trace-99", record.Header("X-Sns-Trace"));
        Assert.Equal("orders", record.Header("X-Sns-Origin"));
    }

    [Fact]
    public async Task UnpackAttributesAsHeadersOnANonSnsBodyStillDelivers()
    {
        var correlation = NewCorrelation();
        var body = JsonSerializer.Serialize(new { correlation, note = "no envelope here" });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-sns-unpack", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/http/sns", record.Path);
        Assert.Equal(body, record.Body);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~HeaderPropagationTests"`
Expected: 4 passed.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/HeaderPropagationTests.cs
git commit -m "test(integration): header and SNS attribute propagation tests"
```

---

### Task 10: FailureAndRetryTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/FailureAndRetryTests.cs`

**Interfaces:**
- Consumes: Task 6 output; queues `it-timeout`, `it-failing`, `it-failing-dlq` from Task 1.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

These are the slow ones. `it-timeout` and `it-failing` have a 5-second visibility timeout, so a rejected message comes back quickly.

```csharp
using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
[Trait("Speed", "Slow")]
public class FailureAndRetryTests
{
    private readonly SinkClient _sink = new();

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private static string JsonBody(string correlation) =>
        JsonSerializer.Serialize(new { correlation });

    [Fact]
    public async Task ASinkThatStallsPastTheTimeoutCausesRedelivery()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-timeout", JsonBody(correlation));

        // The alias has Timeout=2000 against an endpoint that sleeps 6000ms, so
        // redrive abandons every attempt and never deletes the message. With a
        // 5s visibility timeout the second attempt lands well inside the budget.
        var records = await _sink.WaitForDeliveryAsync(
            correlation, TimeSpan.FromSeconds(60), minimumCount: 2);

        Assert.True(records.Count >= 2,
            $"Expected at least 2 attempts, saw {records.Count}.");
        Assert.All(records, r => Assert.Equal("/http/slow", r.Path));
    }

    [Fact]
    public async Task ASinkReturning500CausesRedelivery()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-failing", JsonBody(correlation));

        var records = await _sink.WaitForDeliveryAsync(
            correlation, TimeSpan.FromSeconds(60), minimumCount: 2);

        Assert.True(records.Count >= 2,
            $"Expected at least 2 attempts, saw {records.Count}.");
        Assert.All(records, r => Assert.Equal("/http/fail", r.Path));
    }

    [Fact]
    public async Task AMessageRejectedPastMaxReceiveCountLandsInTheDeadLetterQueue()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();

        // Guard: without a RedrivePolicy this test can never pass, and the failure
        // should say why rather than just timing out.
        var policy = await queues.GetRedrivePolicyAsync("it-failing");
        Assert.False(string.IsNullOrEmpty(policy),
            "it-failing has no RedrivePolicy. Floci may not support DLQ redrive - " +
            "see the Known Risks section of the design doc.");

        await queues.SendAsync("it-failing", JsonBody(correlation));

        // maxReceiveCount=2 with a 5s visibility timeout: two attempts, then the DLQ.
        var message = await queues.WaitForDlqMessageAsync(
            "it-failing-dlq", correlation, TimeSpan.FromSeconds(90));

        Assert.NotNull(message);
        Assert.Contains(correlation, message!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASuccessfullyDeliveredMessageIsRemovedFromItsQueue()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-post", JsonBody(correlation));

        await _sink.WaitForDeliveryAsync(correlation);

        // Delivery happened. The queue must drain, and stay drained - a message that
        // was never deleted would reappear once its visibility timeout expired.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        var depth = -1;
        while (DateTime.UtcNow < deadline)
        {
            depth = await queues.ApproximateDepthAsync("it-http-post");
            if (depth == 0)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(0, depth);

        // No second delivery of the same message.
        var records = await _sink.GetRecordedAsync(correlation);
        Assert.Single(records);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~FailureAndRetryTests"`
Expected: 4 passed, roughly 2-4 minutes.

If `AMessageRejectedPastMaxReceiveCountLandsInTheDeadLetterQueue` fails on its guard assertion, Floci does not implement DLQ redrive. Report it — the spec says to cut this test and test 28 in that case.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/FailureAndRetryTests.cs
git commit -m "test(integration): timeout, failure, retry and DLQ tests"
```

---

### Task 11: TlsTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/TlsTests.cs`

**Interfaces:**
- Consumes: Task 6 output; the `it-https-lax` and `it-https-strict` aliases from Task 4.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

```csharp
using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
public class TlsTests
{
    private readonly SinkClient _sink = new();

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private static string JsonBody(string correlation) =>
        JsonSerializer.Serialize(new { correlation });

    [Fact]
    public async Task IgnoreCertificateErrorsAllowsDeliveryOverSelfSignedHttps()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-https-lax", JsonBody(correlation));

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/http/tls", record.Path);
        Assert.Equal("POST", record.Method);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WithoutIgnoreCertificateErrorsSelfSignedHttpsIsRejected()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-https-strict", JsonBody(correlation));

        // The certificate is self-signed and the alias does not waive validation,
        // so RestSharp's handler refuses the connection on every attempt. Nothing
        // should ever reach the sink. 30s covers several redelivery cycles.
        await _sink.AssertNoDeliveryAsync(correlation, TimeSpan.FromSeconds(30));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~TlsTests"`
Expected: 2 passed.

If `IgnoreCertificateErrorsAllowsDeliveryOverSelfSignedHttps` fails, check that the certificate's SAN includes `DNS:sink` — redrive connects to the container by that name.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/TlsTests.cs
git commit -m "test(integration): TLS certificate handling tests"
```

---

### Task 12: KafkaSinkTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/KafkaSinkTests.cs`

**Interfaces:**
- Consumes: Task 6 output; the Kafka consumer from Task 3.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

```csharp
using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
public class KafkaSinkTests
{
    private readonly SinkClient _sink = new();

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AnSqsMessageReachesTheConfiguredKafkaTopicIntact()
    {
        var correlation = NewCorrelation();
        var body = JsonSerializer.Serialize(new { correlation, payload = "kafka-plain" });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-kafka-plain", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("kafka", record.Transport);
        Assert.Equal("it-kafka-plain", record.Topic);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task CompressionEnabledStillDeliversAnIntactPayload()
    {
        var correlation = NewCorrelation();
        // Repetitive content so Snappy actually has something to compress.
        var payload = string.Concat(Enumerable.Repeat("compress-me-", 2000));
        var body = JsonSerializer.Serialize(new { correlation, payload });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-kafka-compressed", body);

        var records = await _sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("kafka", record.Transport);
        Assert.Equal("it-kafka-compressed", record.Topic);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task SqsMessageAttributesAreNotCarriedToKafka()
    {
        var correlation = NewCorrelation();
        var body = JsonSerializer.Serialize(new { correlation });

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-kafka-plain", body, new Dictionary<string, string>
        {
            ["X-Trace-Id"] = "should-not-survive"
        });

        var records = await _sink.WaitForDeliveryAsync(correlation);
        var record = Assert.Single(records);

        // KafkaMessageProcessor.ProcessMessage accepts an attributes argument and
        // ignores it - it produces a Message<Null, string> with no headers. This
        // test pins that behaviour so a future change to it is a deliberate one.
        Assert.Null(record.Header("X-Trace-Id"));
        Assert.Empty(record.Headers);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~KafkaSinkTests"`
Expected: 3 passed.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/KafkaSinkTests.cs
git commit -m "test(integration): Kafka sink tests"
```

---

### Task 13: ConfigurationReloadTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/ConfigurationReloadTests.cs`

**Interfaces:**
- Consumes: Task 6 output; the admin endpoint from Task 5.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

The `Orchestrator` reconciles every 60 seconds, so each of these waits up to 90. `IAsyncLifetime.DisposeAsync` restores the committed baseline even when a test fails.

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
[Trait("Speed", "Slow")]
public class ConfigurationReloadTests : IAsyncLifetime
{
    private static readonly TimeSpan ReloadBudget = TimeSpan.FromSeconds(90);

    private readonly SinkClient _sink = new();

    public Task InitializeAsync() => Task.CompletedTask;

    // Always put the committed configuration back, even after a failure.
    public Task DisposeAsync() => _sink.ResetConfigAsync();

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private static string JsonBody(string correlation) =>
        JsonSerializer.Serialize(new { correlation });

    private static JsonObject FindAlias(JsonArray config, string alias) =>
        config.OfType<JsonObject>().Single(e => (string?)e["Alias"] == alias);

    [Fact]
    public async Task AnInactiveAliasNeverDrainsItsQueue()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-inactive", JsonBody(correlation));

        // it-inactive is Active:false in the committed config, so no processor
        // exists for it and the message just sits there.
        await _sink.AssertNoDeliveryAsync(correlation, TimeSpan.FromSeconds(30));
        Assert.True(await queues.ApproximateDepthAsync("it-inactive") > 0);
    }

    [Fact]
    public async Task ActivatingAnAliasAtRuntimeDrainsItsQueue()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-inactive", JsonBody(correlation));

        var config = await _sink.GetConfigAsync();
        FindAlias(config, "it-inactive")["Active"] = true;
        await _sink.SetConfigAsync(config);

        var records = await _sink.WaitForDeliveryAsync(correlation, ReloadBudget);

        var record = records[0];
        Assert.Equal("/http/inactive", record.Path);
    }

    [Fact]
    public async Task AnAliasAddedAtRuntimeStartsDelivering()
    {
        var correlation = NewCorrelation();

        using var queues = new TestQueueClient();
        await queues.SendAsync("it-reload", JsonBody(correlation));

        // it-reload has a queue but no alias in the committed config.
        await _sink.AssertNoDeliveryAsync(correlation, TimeSpan.FromSeconds(10));

        var config = await _sink.GetConfigAsync();
        config.Add(new JsonObject
        {
            ["Alias"] = "it-reload",
            ["QueueUrl"] = "http://floci:4566/000000000000/it-reload",
            ["ServiceUrl"] = "http://floci:4566",
            ["Region"] = "us-east-1",
            ["AccessKey"] = "test",
            ["SecretKey"] = "test",
            ["RedriveUrl"] = "http://sink:8080/http/reload",
            ["Active"] = true,
            ["Timeout"] = 10000,
            ["LogLevel"] = "Debug"
        });
        await _sink.SetConfigAsync(config);

        var records = await _sink.WaitForDeliveryAsync(correlation, ReloadBudget);

        Assert.Equal("/http/reload", records[0].Path);
    }

    [Fact]
    public async Task RemovingAnAliasStopsItsProcessor()
    {
        using var queues = new TestQueueClient();

        // Prove the alias is live first.
        var before = NewCorrelation();
        await queues.SendAsync("it-http-put", JsonBody(before));
        await _sink.WaitForDeliveryAsync(before);

        var config = await _sink.GetConfigAsync();
        var remaining = new JsonArray();
        foreach (var entry in config.OfType<JsonObject>())
        {
            if ((string?)entry["Alias"] == "it-http-put")
            {
                continue;
            }
            remaining.Add(JsonNode.Parse(entry.ToJsonString())!);
        }
        await _sink.SetConfigAsync(remaining);

        // Give the reconciliation loop a full cycle to stop the processor.
        await Task.Delay(TimeSpan.FromSeconds(75));

        var after = NewCorrelation();
        await queues.SendAsync("it-http-put", JsonBody(after));
        await _sink.AssertNoDeliveryAsync(after, TimeSpan.FromSeconds(30));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~ConfigurationReloadTests"`
Expected: 4 passed, roughly 5-6 minutes.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/ConfigurationReloadTests.cs
git commit -m "test(integration): runtime configuration reload tests"
```

---

### Task 14: DashboardTests

**Files:**
- Create: `Tests/AWSRedrive.Test.Integration/DashboardTests.cs`

**Interfaces:**
- Consumes: Task 6 output; the redrive dashboard from Task 4.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the tests**

`DashboardServer.GetStatus()` serialises with camelCase-ish property names; the test reads defensively rather than binding to a DTO that would break if the dashboard shape changes.

```csharp
using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

[Collection(IntegrationCollection.Name)]
public class DashboardTests
{
    private readonly SinkClient _sink = new();
    private readonly TestEnvironment _env = TestEnvironment.Instance;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private async Task<JsonElement> GetStatusAsync()
    {
        var json = await _http.GetStringAsync($"{_env.DashboardUrl.TrimEnd('/')}/api/status");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Finds an alias entry anywhere in the status document.</summary>
    private static JsonElement? FindAlias(JsonElement root, string alias)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var field in item.EnumerateObject())
                {
                    if (field.Name.Equals("alias", StringComparison.OrdinalIgnoreCase)
                        && field.Value.ValueKind == JsonValueKind.String
                        && field.Value.GetString() == alias)
                    {
                        return item;
                    }
                }
            }
        }

        return null;
    }

    private static long? ReadNumber(JsonElement element, string name)
    {
        foreach (var field in element.EnumerateObject())
        {
            if (field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && field.Value.ValueKind == JsonValueKind.Number)
            {
                return field.Value.GetInt64();
            }
        }
        return null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        foreach (var field in element.EnumerateObject())
        {
            if (field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && field.Value.ValueKind == JsonValueKind.String)
            {
                return field.Value.GetString();
            }
        }
        return null;
    }

    [Fact]
    public async Task StatusReportsMessageCountsClimbingForAnAlias()
    {
        var before = FindAlias(await GetStatusAsync(), "it-http-post");
        Assert.NotNull(before);
        var sentBefore = ReadNumber(before!.Value, "messagesSent") ?? 0;

        var correlation = NewCorrelation();
        using var queues = new TestQueueClient();
        await queues.SendAsync("it-http-post", JsonSerializer.Serialize(new { correlation }));
        await _sink.WaitForDeliveryAsync(correlation);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        long sentAfter = sentBefore;
        while (DateTime.UtcNow < deadline)
        {
            var current = FindAlias(await GetStatusAsync(), "it-http-post");
            sentAfter = current is null ? sentBefore : ReadNumber(current.Value, "messagesSent") ?? sentBefore;
            if (sentAfter > sentBefore)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.True(sentAfter > sentBefore,
            $"messagesSent for it-http-post did not increase (before={sentBefore}, after={sentAfter}).");
    }

    [Fact]
    public async Task DlqUrlIsAutoDiscoveredFromTheRedrivePolicy()
    {
        using var queues = new TestQueueClient();
        var policy = await queues.GetRedrivePolicyAsync("it-failing");
        Assert.False(string.IsNullOrEmpty(policy),
            "it-failing has no RedrivePolicy. Floci may not support DLQ redrive - " +
            "see the Known Risks section of the design doc.");

        var entry = FindAlias(await GetStatusAsync(), "it-failing");
        Assert.NotNull(entry);

        var dlqUrl = ReadString(entry!.Value, "dlqUrl");
        Assert.False(string.IsNullOrEmpty(dlqUrl),
            "The dashboard did not surface a DlqUrl for it-failing.");
        Assert.Contains("it-failing-dlq", dlqUrl!, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test Tests/AWSRedrive.Test.Integration --filter "FullyQualifiedName~DashboardTests"`
Expected: 2 passed.

If `FindAlias` returns null, inspect the real shape with `curl.exe -s http://localhost:5000/api/status` and adjust the traversal — `DashboardServer.GetStatus()` is the source of truth.

- [ ] **Step 3: Commit**

```bash
git add Tests/AWSRedrive.Test.Integration/DashboardTests.cs
git commit -m "test(integration): dashboard metrics and DLQ discovery tests"
```

---

### Task 15: Documentation and a full run

**Files:**
- Create: `integration-infrastructure/README.md`
- Modify: `README.md` (repo root)
- Modify: `Makefile`

**Interfaces:**
- Consumes: everything.
- Produces: the documented entry point.

- [ ] **Step 1: Write `integration-infrastructure/README.md`**

````markdown
# Integration test infrastructure

Everything the integration suite needs to run, as Docker containers. Start it
from here, then run the tests from Visual Studio or the CLI.

```powershell
cd integration-infrastructure
./start.ps1                                   # build and start; waits until ready
dotnet test ../Tests/AWSRedrive.Test.Integration
./stop.ps1                                    # containers, volumes and built images
```

`./stop.ps1 -All` additionally prunes unused Docker data machine-wide. It asks
first.

## What is running

| Service | Host address | Role |
|---|---|---|
| `floci` | http://localhost:4566 | SQS emulator. Tests send messages here. |
| `kafka` | localhost:29092 | Redrive's Kafka destination. |
| `sink` | http://localhost:8080, https://localhost:8443 | Records every delivery; serves it back to tests. |
| `redrive` | http://localhost:5000 | The system under test. Its dashboard. |

`floci-init` and `kafka-init` are one-shot containers that create the queues and
topics, then exit. They are the readiness gate — `redrive` waits for both.

## Poking at it by hand

```powershell
curl.exe -s http://localhost:8080/recorded         # the last 100 deliveries
curl.exe -s http://localhost:8080/recorded/<guid>  # one correlation id
curl.exe -s http://localhost:5000/api/status       # redrive's own view
docker compose logs -f redrive
```

## How the tests stay isolated

Redrive reads `config.json` once a minute, so per-test configuration is not
practical. Instead the 16 aliases in `redrive/config.json` cover every
configuration shape redrive supports, and tests isolate by putting a fresh GUID
in each message. The sink indexes deliveries by that GUID, so tests never see
each other's traffic.

The sink also mounts `redrive/config.json` read-write and exposes
`/admin/redrive-config`, which is how the reload tests change redrive's
configuration without needing to know where the repository lives on disk.

## Changing the configuration

Edit `redrive/config.json`, then restart just redrive:

```powershell
docker compose restart redrive
```

Adding an alias also means adding its queue to `floci/seed.sh`, which requires a
full `./stop.ps1; ./start.ps1` because Floci stores queues in memory.
````

- [ ] **Step 2: Add a section to the repo `README.md`**

Insert after the existing build/test content:

```markdown
## Integration tests

The integration suite runs against a Docker stack: an SQS emulator, Kafka, a
recording sink service and AWSRedrive itself.

```powershell
cd integration-infrastructure
./start.ps1
dotnet test Tests/AWSRedrive.Test.Integration
./stop.ps1
```

See `integration-infrastructure/README.md` for details.
```

- [ ] **Step 3: Add Makefile targets**

```makefile
INTEGRATION = Tests/AWSRedrive.Test.Integration/AWSRedrive.Test.Integration.csproj

integration-up:
	pwsh -File integration-infrastructure/start.ps1
integration-down:
	pwsh -File integration-infrastructure/stop.ps1
integration-test:
	dotnet test $(INTEGRATION) -c Debug
integration-test-fast:
	dotnet test $(INTEGRATION) -c Debug --filter "Speed!=Slow"
```

Add `integration-up integration-down integration-test integration-test-fast` to the `.PHONY` line, and add a line to the `help` target:

```makefile
	@echo "Integration:  integration-up, integration-test, integration-test-fast, integration-down"
```

- [ ] **Step 4: Run the whole suite from a cold stack**

```powershell
cd integration-infrastructure
./stop.ps1
./start.ps1
cd ..
dotnet test Tests/AWSRedrive.Test.Integration
```
Expected: 28 passed, 0 failed. Roughly 10-12 minutes, dominated by the reload tests.

Then confirm the fast subset:
```powershell
dotnet test Tests/AWSRedrive.Test.Integration --filter "Speed!=Slow"
```
Expected: 21 passed (28 minus the 4 reload, 4 failure-and-retry, and 1 strict-TLS
tests — note `TlsTests` carries the trait at method level, so 28 - 9 = 19 if the
whole `FailureAndRetryTests` and `ConfigurationReloadTests` classes are excluded).
Record the real number in the README once observed.

- [ ] **Step 5: Verify the unit tests still pass**

Run: `dotnet test Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj -c Debug`
Expected: all pass. Nothing under `Projects/` changed, so this is a regression check on the solution wiring only.

- [ ] **Step 6: Commit**

```bash
git add integration-infrastructure/README.md README.md Makefile
git commit -m "docs(integration): document the integration stack and add make targets"
```

---

## Self-Review Notes

**Spec coverage.** All six architecture sections map to Tasks 1-5. All 28 tests map to Tasks 7-14. The scripts are Task 1 (created) and Tasks 2-4 (extended). The test project layout is Task 6. Documentation is Task 15. The three Known Risks each have an explicit verification step: queue-URL host mismatch in Task 4 Step 6, region resolution in Task 4 Step 6, Floci DLQ fidelity in Task 1 Step 7 with guard assertions in Tasks 10 and 14.

**One deliberate deviation from the spec.** The spec listed `it-reload` among the 16 aliases; the committed `config.json` in Task 4 has 15 entries, because `it-reload` exists only as a queue until a test adds the alias. The queue is seeded in Task 1. This is what the spec's own description of test 25 requires.

**Known imprecision.** Task 15 Step 4's expected count for `--filter "Speed!=Slow"` depends on whether xUnit applies class-level traits for filtering the way method-level ones are applied. The step says to record the observed number rather than assert a guessed one.
