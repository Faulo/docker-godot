ARG OS_BASE=bookworm-slim
ARG DEBIAN_FRONTEND=noninteractive

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build

WORKDIR /source

COPY common/Godot/ .

RUN dotnet publish Godot.csproj \
        --configuration Release \
        --runtime linux-x64 \
        --self-contained true \
        --property:PublishSingleFile=true \
        --output /out

FROM debian:${OS_BASE}

USER root
WORKDIR /godot

COPY --from=build --chmod=755 /out/godot /godot/godot

ENV PATH="/godot:${PATH}"
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

# Utilities
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        libasound2 \
        libdbus-1-3 \
        libegl1 \
        libfontconfig1 \
        libgl1 \
        libice6 \
        libsm6 \
        libvulkan1 \
        libx11-6 \
        libxext6 \
        libxfixes3 \
        libxi6 \
        libxkbcommon0 \
        libxrender1 \
        libxxf86vm1 \
        xz-utils && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

RUN install -d /godot/binaries /godot/export_templates /blender

VOLUME ["/godot/binaries", "/godot/export_templates", "/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["godot", "health"]

CMD ["godot"]
