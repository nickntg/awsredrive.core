CONFIG ?= Release
OUTPUT ?= ./publish

# Auto-detect runtime
UNAME_S := $(shell uname -s 2>/dev/null || echo Windows)
UNAME_M := $(shell uname -m 2>/dev/null || echo x86_64)
ifeq ($(UNAME_S),Darwin)
    RUNTIME ?= $(if $(filter arm64,$(UNAME_M)),osx-arm64,osx-x64)
else ifeq ($(UNAME_S),Linux)
    RUNTIME ?= $(if $(filter aarch64,$(UNAME_M)),linux-arm64,linux-x64)
else
    RUNTIME ?= win-x64
endif

# Docker settings
DOCKER_PLATFORM = $(if $(filter %arm64,$(RUNTIME)),linux/arm64,linux/amd64)
DOCKER_RUNTIME = $(if $(filter %arm64,$(RUNTIME)),linux-arm64,linux-x64)
DOCKER_REGISTRY ?=
DOCKER_IMAGE ?= awsredrive
DOCKER_TAG ?= latest
FULL_IMAGE = $(if $(DOCKER_REGISTRY),$(DOCKER_REGISTRY)/$(DOCKER_IMAGE),$(DOCKER_IMAGE))

# Docker SDK build settings
SDK_IMAGE ?= mcr.microsoft.com/dotnet/sdk:8.0
BUILD_RUNTIME ?= linux-x64

# Projects
CONSOLE = Projects/AWSRedrive.console/AWSRedrive.console.csproj
SERVICE = Projects/AWSRedrive.LinuxService/AWSRedrive.LinuxService.csproj
TESTS = Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj
PUBLISH = -c $(CONFIG) -r $(RUNTIME) --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false

.PHONY: run watch test console service all sign image image-push clean help

help:
	@echo "Development:  run, watch, test, test-watch, logs"
	@echo "Build:        console, service, all (add -quick to skip tests)"
	@echo "Docker SDK:   docker-build-console, docker-build-service, docker-build-all, docker-test"
	@echo "Docker:       docker-console, docker-service, image, image-push, image-run"
	@echo "macOS:        sign"
	@echo "Options:      RUNTIME=$(RUNTIME) CONFIG=$(CONFIG) BUILD_RUNTIME=$(BUILD_RUNTIME)"

# Development
run:
	cd Projects/AWSRedrive.console && dotnet run -c Debug
watch:
	cd Projects/AWSRedrive.console && dotnet watch run -c Debug
test:
	dotnet test $(TESTS) -c Debug
test-watch:
	dotnet watch test --project $(TESTS)
logs:
	tail -f Projects/AWSRedrive.console/bin/Debug/net8.0/logs/awsredrive.log 2>/dev/null | jq . || \
	tail -f Projects/AWSRedrive.console/bin/Debug/net8.0/logs/awsredrive.log

# Build (local .NET SDK)
console: test
	dotnet publish $(CONSOLE) $(PUBLISH) -o $(OUTPUT)/console-$(RUNTIME)
service: test
	dotnet publish $(SERVICE) $(PUBLISH) -o $(OUTPUT)/service-$(RUNTIME)
all: console service

console-quick:
	dotnet publish $(CONSOLE) $(PUBLISH) -o $(OUTPUT)/console-$(RUNTIME)
service-quick:
	dotnet publish $(SERVICE) $(PUBLISH) -o $(OUTPUT)/service-$(RUNTIME)
all-quick: console-quick service-quick

# Docker SDK build (no local .NET needed, cross-compile from Mac ARM to Linux x64)
docker-build-console:
	docker run --rm -v $(PWD):/src -w /src $(SDK_IMAGE) \
		dotnet publish $(CONSOLE) -c $(CONFIG) -r $(BUILD_RUNTIME) --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o /src/$(OUTPUT)/console-$(BUILD_RUNTIME)

docker-build-service:
	docker run --rm -v $(PWD):/src -w /src $(SDK_IMAGE) \
		dotnet publish $(SERVICE) -c $(CONFIG) -r $(BUILD_RUNTIME) --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o /src/$(OUTPUT)/service-$(BUILD_RUNTIME)

docker-build-all: docker-test docker-build-console docker-build-service

docker-build-console-quick:
	docker run --rm -v $(PWD):/src -w /src $(SDK_IMAGE) \
		dotnet publish $(CONSOLE) -c $(CONFIG) -r $(BUILD_RUNTIME) --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o /src/$(OUTPUT)/console-$(BUILD_RUNTIME)

docker-build-service-quick:
	docker run --rm -v $(PWD):/src -w /src $(SDK_IMAGE) \
		dotnet publish $(SERVICE) -c $(CONFIG) -r $(BUILD_RUNTIME) --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o /src/$(OUTPUT)/service-$(BUILD_RUNTIME)

docker-build-all-quick: docker-build-console-quick docker-build-service-quick

docker-test:
	docker run --rm -v $(PWD):/src -w /src $(SDK_IMAGE) \
		dotnet test $(TESTS) -c Debug

# macOS signing
sign:
	codesign --sign - --force --preserve-metadata=entitlements,requirements,flags,runtime $(OUTPUT)/console-$(RUNTIME)/AWSRedrive.console

# Docker export (multi-stage build)
docker-console:
	docker build -f Dockerfile --target console --platform $(DOCKER_PLATFORM) --build-arg SKIP_TESTS=true --output=$(OUTPUT)/console-$(DOCKER_RUNTIME) .
docker-service:
	docker build -f Dockerfile --target service --platform $(DOCKER_PLATFORM) --build-arg SKIP_TESTS=true --output=$(OUTPUT)/service-$(DOCKER_RUNTIME) .

# Docker image
image:
	docker build -f Dockerfile.image --target console-image --platform $(DOCKER_PLATFORM) -t $(FULL_IMAGE):$(DOCKER_TAG) .
image-service:
	docker build -f Dockerfile.image --target service-image --platform $(DOCKER_PLATFORM) -t $(FULL_IMAGE)-service:$(DOCKER_TAG) .
image-push:
	docker push $(FULL_IMAGE):$(DOCKER_TAG)
image-run:
	docker run --rm -it -v $(PWD)/config.json:/app/config.json:ro -v $(PWD)/appsettings.json:/app/appsettings.json:ro -p 5000:5000 $(FULL_IMAGE):$(DOCKER_TAG)

# Clean
clean:
	rm -rf $(OUTPUT) Projects/*/bin Projects/*/obj Tests/*/bin Tests/*/obj