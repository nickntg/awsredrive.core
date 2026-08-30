"""Recording sink for the AWSRedrive integration test suite.

Records every delivery AWSRedrive makes - over HTTP and off the Kafka topics -
and serves them back to the tests keyed by a correlation id. Also exposes an
admin endpoint that rewrites redrive's config.json, which is how the reload
tests change configuration without needing to know where the repository lives
on disk.

Runs two uvicorn servers in one process: plain HTTP on 8080 and TLS on 8443,
sharing a single in-memory store.
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

KAFKA_BOOTSTRAP = os.environ.get("KAFKA_BOOTSTRAP", "")
KAFKA_TOPICS = [t for t in os.environ.get("KAFKA_TOPICS", "").split(",") if t]

REDRIVE_CONFIG_PATH = os.environ.get("REDRIVE_CONFIG_PATH", "/redrive-config/config.json")
REDRIVE_CONFIG_BASELINE = os.environ.get("REDRIVE_CONFIG_BASELINE", "/app/config.baseline.json")


# --------------------------------------------------------------------------
# Store
# --------------------------------------------------------------------------
_lock = threading.Lock()
_by_correlation = defaultdict(list)
_all_records = []


def _now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def extract_correlation(headers, body, query):
    """Header, then JSON body field, then query parameter.

    The query case is what covers UseGET: redrive unwraps the JSON body into
    query parameters and sends no body at all.
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


def record(transport, method, path, query, headers, body, topic=None):
    """Store one delivery. The correlation id decides which bucket it lands in."""
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
    entry["correlation"] = extract_correlation(headers, body, query)

    with _lock:
        _all_records.append(entry)
        if entry["correlation"]:
            _by_correlation[entry["correlation"]].append(entry)

    return entry


# --------------------------------------------------------------------------
# Routes
# --------------------------------------------------------------------------
router = APIRouter()


@router.get("/health")
async def health():
    with _lock:
        total = len(_all_records)
    return {"status": "ok", "recorded": total}


@router.get("/recorded")
async def recorded_all(limit: int = 100):
    """Debugging aid for humans poking at the stack. Tests do not use it."""
    with _lock:
        return list(_all_records[-limit:])


@router.get("/recorded/{correlation_id}")
async def recorded(correlation_id: str):
    with _lock:
        return list(_by_correlation.get(correlation_id, []))


@router.delete("/recorded/{correlation_id}")
async def clear_recorded(correlation_id: str):
    with _lock:
        removed = len(_by_correlation.pop(correlation_id, []))
    return {"removed": removed}


# --------------------------------------------------------------------------
# Admin: redrive configuration
# --------------------------------------------------------------------------
def _write_config_in_place(entries):
    """Rewrite config.json without replacing the inode.

    A rename-based atomic write would break the bind mount into the redrive
    container - it would keep reading the old inode forever. Truncate and write
    is deliberate here.
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


# --------------------------------------------------------------------------
# Recording routes
# --------------------------------------------------------------------------
def _challenge_for(path, headers):
    """Return None when the request is allowed, or a WWW-Authenticate value."""
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
            "{0}:{1}".format(EXPECTED_BASIC_USER, EXPECTED_BASIC_PASSWORD).encode()
        ).decode()
        if headers.get("authorization") != "Basic " + expected:
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
        return Response(status_code=404, content="not a recording route")

    raw = await request.body()
    body = raw.decode("utf-8", errors="replace") if raw else ""
    headers = {k.lower(): v for k, v in request.headers.items()}
    query = dict(request.query_params)

    # Record before deciding the outcome, so retry tests can count attempts.
    record("http", request.method, path, query, headers, body)

    delay_ms = query.get("delay")
    if delay_ms:
        await asyncio.sleep(int(delay_ms) / 1000.0)

    challenge = _challenge_for(path, headers)
    if challenge:
        return Response(status_code=401, headers={"WWW-Authenticate": challenge})

    status = query.get("status")
    if status:
        return Response(status_code=int(status), content="forced " + status)

    return Response(status_code=200, content="ok")


# --------------------------------------------------------------------------
# Kafka consumer
# --------------------------------------------------------------------------
def _decode(value):
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    return value


def consume_kafka():
    """Record everything redrive produces to the test topics.

    Runs on a daemon thread. Failures are logged and retried rather than raised;
    the HTTP half of the sink must keep working regardless.
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
    print("kafka consumer subscribed to {0} via {1}".format(KAFKA_TOPICS, KAFKA_BOOTSTRAP))

    while True:
        try:
            msg = consumer.poll(1.0)
            if msg is None:
                continue
            if msg.error():
                if msg.error().code() != KafkaError._PARTITION_EOF:
                    print("kafka consumer error: {0}".format(msg.error()))
                continue

            raw = msg.value()
            body = raw.decode("utf-8", errors="replace") if raw else ""
            headers = {k: _decode(v) for k, v in (msg.headers() or [])}
            record("kafka", None, None, None, headers, body, topic=msg.topic())
        except Exception as exc:  # keep the thread alive whatever happens
            print("kafka consumer exception: {0}".format(exc))


def start_kafka_consumer():
    if not KAFKA_BOOTSTRAP or not KAFKA_TOPICS:
        print("kafka consumer disabled (KAFKA_BOOTSTRAP/KAFKA_TOPICS unset)")
        return
    threading.Thread(target=consume_kafka, name="kafka-consumer", daemon=True).start()


# --------------------------------------------------------------------------
# Application
# --------------------------------------------------------------------------
def build_app():
    app = FastAPI(title="AWSRedrive integration sink", docs_url=None, redoc_url=None)
    app.include_router(router)
    return app


async def main():
    start_kafka_consumer()
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

    print(
        "sink listening on http://0.0.0.0:{0} and https://0.0.0.0:{1}".format(
            HTTP_PORT, HTTPS_PORT
        )
    )
    await asyncio.gather(plain.serve(), secure.serve())


if __name__ == "__main__":
    asyncio.run(main())
