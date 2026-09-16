FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build

WORKDIR /solution

COPY . .

RUN dotnet publish \
        --configuration Release \
        --runtime linux-x64 \
        --self-contained true \
        --property:PublishSingleFile=true \
        --property:UseAppHost=true \
        --property:DebugType=None

FROM debian:trixie-slim

ARG DEBIAN_FRONTEND=noninteractive

USER root
WORKDIR /godot

COPY --from=build --chmod=755 /solution/common/Godot/bin/Release/net9.0/linux-x64/publish/ /godot/

ENV PATH="/godot:${PATH}"
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

# Utilities
COPY linux/godot.packages /tmp/godot.packages

RUN apt-get update \
 && xargs apt-get install -y --no-install-recommends < /tmp/godot.packages \
 && rm -f /tmp/godot.packages \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

RUN install -d /godot/binaries /godot/export_templates /blender

VOLUME ["/godot/binaries", "/godot/export_templates", "/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["godot", "health"]

CMD ["godot", "help"]
