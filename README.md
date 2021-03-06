AWSRedrive is an application that allows HTTP backend services to receive asynchronous messages posted to SQS queues without requiring the services to use the AWS SDK.

A typical method to send asynchronous messages to backend services using AWS services is to couple the [Amazon SNS](https://aws.amazon.com/sns/) and [Amazon SQS](https://aws.amazon.com/sqs/) services together. Applications that want to send a message to a backend service will post it to an SNS topic. A queue is subscribed to this topic and can hold the message there until it is delivered to the backend service.

Why not subscribe the HTTP backend service directly to the SNS topic? Well, that can be done, however in this mode SNS will hold a message only up to one hour until it is delivered; if one hour passes and the message is not successfully received by the HTTP service, SNS will discard it and the message will be lost. If you want to have more flexibility, safety and guaranteed delivery with the messages you want to send, using a queue that can hold the messages for far longer is a better choice but then you need something to cover the last mile between the queue and the HTTP service. AWSRedrive was build with that scenario in mind (you can read more info [here](http://engineering.pamediakopes.gr/2015/10/12/sns-a-love-and-hate-story/)).

![What AWSRedrive does](https://github.com/nickntg/awsredrive.core/blob/master/schematic.png)

Configuration is stored in config.json and is periodically read by AWSRedrive, therefore changes take effect without requiring a restart. AWSRedrive can currently post to simple HTTP(S) endpoints and to AWS Gateway endpoints that are using an authentication token.
