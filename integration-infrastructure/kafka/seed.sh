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
