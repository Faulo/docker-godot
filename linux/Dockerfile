FROM debian:bookworm-slim

ARG DEBIAN_FRONTEND=noninteractive

USER root
WORKDIR /var/workspace

ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        jq \
        libasound2 \
        libdbus-1-3 \
        libegl1 \
        libfontconfig1 \
        libgl1 \
        libice6 \
        libsm6 \
        libx11-6 \
        libxext6 \
        libxfixes3 \
        libxi6 \
        libxkbcommon0 \
        libxrender1 \
        libxxf86vm1 \
        unzip \
        util-linux \
        xz-utils && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

COPY --chmod=755 common/setup-linux.sh /usr/local/lib/docker-godot/setup
COPY --chmod=755 linux/godot.sh /usr/local/bin/godot
COPY --chmod=755 linux/blender.sh /usr/local/bin/blender

# Keep Godot's standard data path while placing the large templates on their
# own volume.
RUN install -d /godot/binaries /godot/export_templates /blender /root/.local/share/godot && \
    ln -s /godot/export_templates /root/.local/share/godot/export_templates

VOLUME ["/godot/binaries", "/godot/export_templates", "/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["/usr/local/lib/docker-godot/setup", "health"]

CMD ["godot"]
