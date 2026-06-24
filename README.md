AWSRedrive is an application that allows HTTP backend services or Kafka topics to receive asynchronous messages posted to SQS queues without requiring the services to use the AWS SDK.

A typical method to send asynchronous messages to backend services using AWS services is to couple the [Amazon SNS](https://aws.amazon.com/sns/) and [Amazon SQS](https://aws.amazon.com/sqs/) services together. Applications that want to send a message to a backend service will post it to an SNS topic. A queue is subscribed to this topic and can hold the message there until it is delivered to the backend service.

Why not subscribe the HTTP backend service directly to the SNS topic? Well, that can be done, however in this mode SNS will hold a message only up to one hour until it is delivered; if one hour passes and the message is not successfully received by the HTTP service, SNS will discard it and the message will be lost. If you want to have more flexibility, safety and guaranteed delivery with the messages you want to send, using a queue that can hold the messages for far longer is a better choice but then you need something to cover the last mile between the queue and the HTTP service. AWSRedrive was build with that scenario in mind (you can read more info [here](https://web.archive.org/web/20200217121323/http://engineering.pamediakopes.gr/2015/10/12/sns-a-love-and-hate-story/)).

![What AWSRedrive does](https://github.com/nickntg/awsredrive.core/blob/master/schematic.png)

Configuration is stored in config.json and is periodically read by AWSRedrive, therefore changes take effect without requiring a restart. AWSRedrive can currently post to simple HTTP(S) endpoints and to AWS Gateway endpoints that are using an authentication token.

Here's a sample configuration entry for reading a queue and posting the messages found there to an endpoint:

```json
{
  "Alias": "#1",
  "QueueUrl": "https://sqs.eu-west-1.amazonaws.com/accountid/inputqueue1",
  "RedriveUrl": "http://nohost.com/",
  "Region": "eu-west-1",
  "Active": true,
  "Timeout": 10000,
  "LogLevel": "Info",
  "ServiceUrl": "https://www.google.com" 
}
```

Here are the elements of a configuration entry:
* **Alias**. This is a unique name for each configuration entry.
* **AccessKey**. The AWS access key to use when accessing SQS. If an access key is not found, AWSRedrive will not use one and rely on the AWS SDK to determine how to connect to SQS.
* **SecretKey**. The AWS secret key to use when accessing SQS.
* **Profile**. The AWS profile to use when accessing SQS. If specified, AWSRedrive will use credentials from the AWS credentials file.
* **QueueUrl**. The URL of the SQS queue to read.
* **Region**. The region of the SQS queue.
* **RedriveUrl**. The endpoint of the service to post SQS messages to.
* **RedriveScript**. The powershell script to execute with content of SQS messages. This parameter comes into effect if RedriveUrl is empty.
* **RedriveKafkaTopic**. The Kafka topic to send content of SQS messages to. This parameter comes into effect if RedriveUrl and RedriveScript are both empty.
* **KafkaBootstrapServers**. The bootstrap servers to use when connecting to Kafka as a producer.
* **KafkaClientId**. The client id to use when connecting to Kafka. If not specified, a value of _redrive_ is used.
* **UseKafkaCompression**. If set to True, Snappy compression is enabled when sending messages to the Kafka topic.
* **AwsGatewayToken**. If present, AWSRedrive will add this value to an _x-api-key_ header before posting SQS messages to the configured service endpoint. Useful when the service is exposed via AWS Gateway and a token is required.
* **AuthToken**. If present, AWSRedrive will add this value to the authorization header before posting SQS messages to the configured service endpoint.
* **BasicAuthUserName**. If present, AWSRedrive will use this value and the one specified in BasicAuthPassword to perform basic authentication when posting messages to the configured service endpoint.
* **BasicAuthPassword**. See above.
* **Active**. Set to True to enable the configuration, False to disable it.
* **UseDELETE**. If set to True, AWSRedrive will use DELETE instead of POST when sending messages to the configured service endpoint. Any JSON body will be included.
* **UseGET**. If set to True, AWSRedrive will use GET when sending messages to the configured service endpoint. When doing that, AWSRedrive will unwrap any JSON object contained in the SQS message and turn all JSON fields to query parameters.
* **UsePUT**. If set to True, AWSRedrive will use PUT instead of POST when sending messages to the configured service endpoint.
* **Timeout**. Service timeout in milliseconds to observe when sending messages to the configured service endpoint.
* **IgnoreCertificateErrors**. If set to True, AWSRedrive will ignore any certificate errors when connecting to the configured service endpoint.
* **UnpackAttributesAsHeaders**. If set to True, AWSRedrive will try to treat the incoming message as being an [SNS envelope](https://docs.aws.amazon.com/sns/latest/dg/sns-message-and-json-formats.html), then unpack message attributes and transfer them as HTTP headers.
* **ServiceUrl**. If configured, this value will be passed to the ServiceURL property of the AWS SDK. This is useful when working with [LocalStack](https://localstack.cloud/) instead of AWS.
* **LogLevel**. The log level for this entry (Trace, Debug, Info, Warn, Error, Fatal). If not specified, uses the global `DefaultLogLevel` from appsettings.json.

## Application Settings

Application-wide settings are stored in appsettings.json:

```json
{
  "DefaultLogLevel": "Error",
  "Dashboard": { "Enabled": true, "Port": 5000, "RefreshIntervalMs": 1000 },
  "Metrics": { "Enabled": true, "IntervalSeconds": 60 }
}
```

* **DefaultLogLevel**. Default log level for aliases without explicit LogLevel (default: Error).
* **Dashboard.Enabled**. Enables the web dashboard.
* **Dashboard.Port**. Dashboard port (default: 5000).
* **Dashboard.RefreshIntervalMs**. Dashboard refresh interval (default: 1000ms).
* **Metrics.Enabled**. Enables periodic metrics logging.
* **Metrics.IntervalSeconds**. Metrics logging interval (default: 60s).

## Dashboard

AWSRedrive includes a web dashboard for real-time monitoring at `http://localhost:5000`. Features:

- Status overview of all queue processors
- Message counts with sparkline graphs
- Summary bar with totals and error count
- Sorting by name, received, failed, uptime, or recent activity
- Error highlighting with red cards
- Runtime log level changes (temporary, reverts after 30 minutes)
- Direct links to AWS SQS Console

Log levels can also be changed via API:

```bash
curl -X POST "http://localhost:5000/api/loglevel/MyAlias?level=Debug"
```

**Note:** Log level changes from dashboard/API are temporary and revert after 30 minutes or on app restart. For permanent changes, edit config.json.

## Logging

AWSRedrive uses structured JSON logging to file (`logs/awsredrive.log`):

```json
{"@timestamp":"2024-01-15T10:30:45.123Z","level":"INFO","logger":"QueueProcessor","message":"Message received","alias":"MyAlias"}
```

Log files are archived daily with date suffix (e.g., `awsredrive.2024-01-14.log`) and kept for 7 days.

## Building

Prerequisites: .NET 8 SDK or Docker.

```bash
make help    # Show all available commands
```

### Development

```bash
make run     # Run console app locally
make watch   # Run with hot reload
make test    # Run unit tests
```

### Build with Local .NET SDK

```bash
make all                       # Build console + service (runs tests first)
make all-quick                 # Build without tests
make console RUNTIME=linux-x64 # Cross-compile for specific runtime
```

### Build with Docker SDK (no local .NET required)

Ideal for building Linux binaries from Mac/Windows:

```bash
make docker-build-all          # Test + build console + service for linux-x64
make docker-build-all-quick    # Build without tests
make docker-test               # Run tests in Docker
```

Output: `./publish/service-linux-x64/` and `./publish/console-linux-x64/`

### Docker Image

```bash
make image                     # Build container image
make image-run                 # Run container locally
make image-push DOCKER_REGISTRY=ghcr.io/user DOCKER_TAG=1.0.0
```

### Options

| Variable | Default | Description |
|----------|---------|-------------|
| `RUNTIME` | auto-detected | Target runtime (linux-x64, linux-arm64, osx-arm64, win-x64) |
| `BUILD_RUNTIME` | linux-x64 | Target for docker-build-* commands |
| `CONFIG` | Release | Build configuration |

## Running with Docker

```bash
docker run --rm -it \
    -v ./config.json:/app/config.json:ro \
    -v ./appsettings.json:/app/appsettings.json:ro \
    -p 5000:5000 \
    awsredrive:latest
```

## Running as a Linux Service

Build with `make docker-build-service` or `make service RUNTIME=linux-x64`, then create `/etc/systemd/system/awsredrive.service`:

```ini
[Unit]
Description=AWSRedrive Service
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/awsredrive
ExecStart=/opt/awsredrive/AWSRedrive.LinuxService
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Enable with `sudo systemctl enable --now awsredrive`.