FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETPLATFORM
ARG SKIP_TESTS=false
WORKDIR /src
COPY Projects/ ./Projects/
COPY Solutions/ ./Solutions/
COPY Tests/ ./Tests/

RUN if [ "$SKIP_TESTS" != "true" ]; then dotnet test Tests/AWSRedrive.Tests.Unit/AWSRedrive.Tests.Unit.csproj -c Release; fi
RUN case "$TARGETPLATFORM" in "linux/arm64") echo "linux-arm64" ;; *) echo "linux-x64" ;; esac > /tmp/runtime

FROM build AS console-build
RUN dotnet publish Projects/AWSRedrive.console/AWSRedrive.console.csproj -c Release -r $(cat /tmp/runtime) --self-contained -p:PublishSingleFile=true -o /out

FROM build AS service-build
RUN dotnet publish Projects/AWSRedrive.LinuxService/AWSRedrive.LinuxService.csproj -c Release -r $(cat /tmp/runtime) --self-contained -p:PublishSingleFile=true -o /out

FROM scratch AS console
COPY --from=console-build /out /

FROM scratch AS service
COPY --from=service-build /out /
