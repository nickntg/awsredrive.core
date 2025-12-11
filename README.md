AWSRedrive is an application that allows HTTP backend services or Kafka topics to receive asynchronous messages posted to SQS queues without requiring the services to use the AWS SDK.

A typical method to send asynchronous messages to backend services using AWS services is to couple the [Amazon SNS](https://aws.amazon.com/sns/) and [Amazon SQS](https://aws.amazon.com/sqs/) services together. Applications that want to send a message to a backend service will post it to an SNS topic. A queue is subscribed to this topic and can hold the message there until it is delivered to the backend service.

Why not subscribe the HTTP backend service directly to the SNS topic? Well, that can be done, however in this mode SNS will hold a message only up to one hour until it is delivered; if one hour passes and the message is not successfully received by the HTTP service, SNS will discard it and the message will be lost. If you want to have more flexibility, safety and guaranteed delivery with the messages you want to send, using a queue that can hold the messages for far longer is a better choice but then you need something to cover the last mile between the queue and the HTTP service. AWSRedrive was build with that scenario in mind (you can read more info [here](https://web.archive.org/web/20200217121323/http://engineering.pamediakopes.gr/2015/10/12/sns-a-love-and-hate-story/)).

![What AWSRedrive does](https://github.com/nickntg/awsredrive.core/blob/master/schematic.png)

Configuration is stored in config.json and is periodically read by AWSRedrive, therefore changes take effect without requiring a restart. AWSRedrive can currently post to simple HTTP(S) endpoints and to AWS Gateway endpoints that are using an authentication token.

Here's a sample configuration entry for reading a queue and posting the messages found there to an endpoint:

```js
  {
    "Alias": "#1",
    "QueueUrl": "https://sqs.eu-west-1.amazonaws.com/accountid/inputqueue1",
    "RedriveUrl": "http://nohost.com/",
    "Region": "eu-west-1",
    "Active": true,
    "Timeout": 10000,
    "LogLevel": "Error",
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
* **LogLevel**. The log level for this entry (Trace, Debug, Info, Warn, Error, Fatal). Defaults to Error. Can be changed at runtime via the dashboard or API.

## Application Settings

Application-wide settings are stored in appsettings.json:

```json
{
  "Dashboard": { "Enabled": true, "Port": 5000, "RefreshIntervalMs": 1000 },
  "Metrics": { "Enabled": true, "IntervalSeconds": 60 }
}
```

* **Dashboard.Enabled**. Enables the web dashboard.
* **Dashboard.Port**. Dashboard port (default: 5000).
* **Dashboard.RefreshIntervalMs**. Dashboard refresh interval (default: 1000ms).
* **Metrics.Enabled**. Enables periodic metrics logging.
* **Metrics.IntervalSeconds**. Metrics logging interval (default: 60s).

## Dashboard

AWSRedrive includes a web dashboard for real-time monitoring at `http://localhost:5000`. It provides status of all queue processors, message counts with sparkline graphs, error information, and runtime log level changes. Log levels can also be changed via API:

```bash
curl -X POST "http://localhost:5000/api/loglevel/MyAlias?level=Debug"
```

## Logging

AWSRedrive uses structured JSON logging to console and file (`logs/awsredrive.json`):

```json
{"@timestamp":"2024-01-15T10:30:45.123Z","level":"INFO","logger":"QueueProcessor","message":"Message received","alias":"MyAlias"}
```

## Building

AWSRedrive uses a Makefile. Prerequisites: .NET 8 SDK, optionally Docker.

```bash
make help             # Show all commands

# Development
make run              # Run locally
make watch            # Run with hot reload
make test             # Run tests

# Build
make console          # Build console app
make service          # Build Linux service
make all              # Build both
make console-quick    # Build without tests

# Cross-platform (auto-detected if not specified)
make console RUNTIME=linux-x64
make console RUNTIME=linux-arm64
make console RUNTIME=osx-arm64
make console RUNTIME=win-x64

# Docker
make docker-console   # Export binary via Docker
make image            # Build container image
make image-run        # Run container locally
make image-push DOCKER_REGISTRY=ghcr.io/user DOCKER_TAG=1.0.0

# macOS signing
make sign

# Cleanup
make clean
```

## Running with Docker

```bash
docker run --rm -it \
    -v ./config.json:/app/config.json:ro \
    -v ./appsettings.json:/app/appsettings.json:ro \
    -p 5000:5000 \
    awsredrive:latest
```

## Running as a Linux Service

Build with `make service RUNTIME=linux-x64`, then create `/etc/systemd/system/awsredrive.service`:

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
