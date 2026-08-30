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
