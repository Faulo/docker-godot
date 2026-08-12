FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

WORKDIR /source

COPY common/DockerGodot.csproj common/Program.cs ./

RUN dotnet publish DockerGodot.csproj --configuration Release --output /out

FROM debian:bookworm-slim

ARG DEBIAN_FRONTEND=noninteractive

USER root
WORKDIR /godot

ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

# Debian exposes libfontconfig as a virtual name; install its concrete runtime
# provider explicitly.
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

COPY --from=build --chmod=755 /out/docker-godot /godot/docker-godot
COPY --chmod=755 linux/godot.sh /godot/godot
COPY --chmod=755 linux/blender.sh /godot/blender

# Keep Godot's standard data path while placing the large templates on their
# own volume.
RUN install -d /godot/binaries /godot/export_templates /blender /root/.local/share/godot && \
    ln -s /godot/export_templates /root/.local/share/godot/export_templates && \
    ln -s /godot/godot /usr/local/bin/godot && \
    ln -s /godot/blender /usr/local/bin/blender

VOLUME ["/godot/binaries", "/godot/export_templates", "/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["/godot/docker-godot", "health"]

CMD ["godot"]
