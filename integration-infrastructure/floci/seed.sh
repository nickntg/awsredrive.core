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

  cat > /tmp/attrs.json <<ATTRS
{
  "VisibilityTimeout": "5",
  "RedrivePolicy": "{\"deadLetterTargetArn\":\"$arn\",\"maxReceiveCount\":\"$max\"}"
}
ATTRS

  aws --endpoint-url "$ENDPOINT" sqs create-queue \
    --queue-name "$name" \
    --attributes file:///tmp/attrs.json >/dev/null
}

echo "Creating queues..."
for q in it-http-post it-http-put it-http-delete it-http-get \
         it-auth-token it-gateway-token it-basic-auth it-sns-unpack \
         it-https-lax it-inactive it-activatable it-removable it-reload \
         it-kafka-plain it-kafka-compressed; do
  create_plain "$q"
done

create_with_dlq it-timeout 3
create_with_dlq it-failing 2
create_with_dlq it-https-strict 3

echo "Seed complete:"
aws --endpoint-url "$ENDPOINT" sqs list-queues
