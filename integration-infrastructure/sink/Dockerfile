# Build context is integration-infrastructure/, not sink/, because the redrive
# config baseline is copied in from redrive/.
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
COPY redrive/config.json ./config.baseline.json

EXPOSE 8080 8443
CMD ["python", "-u", "app.py"]
