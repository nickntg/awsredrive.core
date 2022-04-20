AWSRedrive is an application that allows HTTP backend services to receive asynchronous messages posted to SQS queues without requiring the services to use the AWS SDK.

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
    "ServiceUrl":  "https://www.google.com" 
  }
```

Here are the elements of a configuration entry:
* **Alias**. This is a unique name for each configuration entry.
* **AccessKey**. The AWS access key to use when accessing SQS. If an access key is not found, AWSRedrive will not use one and rely on the AWS SDK to determine how to connect to SQS.
* **SecretKey**. The AWS secret key to use when accessing SQS.
* **QueueUrl**. The URL of the SQS queue to read.
* **Region**. The region of the SQS queue.
* **RedriveUrl**. The endpoint of the service to post SQS messages to.
* **RedriveScript**. The powershell script to execute with content of SQS messages. This parameter comes into effect if RedriveUrl is empty.
* **AwsGatewayToken**. If present, AWSRedrive will add this value to an _x-api-key_ header before posting SQS messages to the configured service endpoint. Useful when the service is exposed via AWS Gateway and a token is required.
* **AuthToken**. If present, AWSRedrive will add this value to the authorization header before posting SQS messages to the configured service endpoint.
* **BasicAuthUserName**. If present, AWSRedrive will use this value and the one specified in BasicAuthPassword to perform basic authentication when posting messages to the configured service endpoint.
* **BasicAuthPassword**. See above.
* **Active**. Set to True to enable the configuration, False to disable it.
* **UsePUT**. If set to True, AWSRedrive will use PUT instead of POST when sending messages to the configured service endpoint.
* **Timeout**. Service timeout in milliseconds to observe when sending messages to the configured service endpoint.
* **IgnoreCertificateErrors**. If set to True, AWSRedrive will ignore any certificate errors when connecting to the configured service endpoint.
* **ServiceUrl**. If configured, this value will be passed to the ServiceURL property of the AWS SDK. This is useful when working with [LocalStack](https://localstack.cloud/) instead of AWS.
