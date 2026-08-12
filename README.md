# Godot Docker Image

This repository builds Linux and Windows Docker images that install the requested stable Godot and optional Blender versions when their wrapper commands are invoked. Downloads are stored in volumes and reused by later containers.

## Image Contents

Both image variants provide:

- The standard Godot editor and matching export templates selected by `GODOT_VERSION`.
- Blender selected by `BLENDER_VERSION`, when configured.
- Thin `godot` and `blender` wrapper commands that install and launch the selected versions.
- The shared `/godot/docker-godot` setup executable used by both wrappers and the health check.

The image currently supports standard Godot 4 builds. Godot .NET/Mono builds and a selector for choosing between standard and .NET builds will be added separately.

## Runtime Setup

The image is designed for direct one-off use:

```text
docker run --rm --env GODOT_VERSION=4 faulo/godot:latest godot --version
```

The working directory is `/godot` on Linux and `C:/godot` on Windows. All image-owned wrapper and setup files live there; only command-discovery links are placed outside that directory on Linux.

The `godot` wrapper performs the following work before starting the selected Godot executable:

1. If `BLENDER_VERSION` is set, resolve and install Blender.
2. Resolve and install Godot and its matching export templates.
3. Publish the readiness state consumed by the Docker health check.
4. Start Godot with the original arguments and return its exit code.

Godot imports `.blend` files by invoking an executable configured in its editor settings. When Blender is selected, the `godot` wrapper preserves the other editor settings and points `filesystem/import/blender/blender_path` at the resolved versioned Blender executable before Godot starts. This lets Godot manage Blender's persistent import process directly. The `blender` wrapper can also be invoked directly and installs only Blender.

Installations use a lock in each shared volume, resume interrupted downloads into temporary directories, verify the publisher-provided SHA-512 or SHA-256 checksum, and publish a completed version directory atomically. This permits concurrent containers to share the same named volumes safely. Version resolution, locking, downloads, verification, readiness, editor configuration, and process launching share one C# implementation across Linux and Windows; only platform paths, artifact names, and archive extraction differ.

The Docker health check only reports wrapper readiness; it never performs installation. The wrapper publishes readiness immediately before the requested Godot or Blender process starts, and Docker reports the container as `healthy` when the next probe observes it. Short-lived commands may exit before Docker runs its first health probe, while still returning the wrapped command's exit status normally.

## Configuration

The wrappers use these environment variables:

- `GODOT_VERSION` is required by `godot` and selects the standard Godot editor and export templates.
- `BLENDER_VERSION` is required by `blender`. It is optional for `godot`; when set, Blender is installed before Godot starts.

Only stable releases are considered. A selector that cannot be resolved to a stable release is an error, as is an invalid selector.

## Version Selectors

Selectors contain one to three numeric components and use prefix-compatible semantics:

- `4` selects the latest stable `4.x.y` release.
- `4.3` selects the latest stable `4.3.x` release.
- `4.3.0` selects exactly `4.3.0`.

Godot itself labels a release without a patch component, such as `4.3-stable`, where semantic versioning would normally write `4.3.0`. The selector still treats `GODOT_VERSION=4.3` as the complete `4.3.x` series and `GODOT_VERSION=4` as the complete `4.x.y` series.

Godot installations use the exact version identifier expected by their export templates. For example, standard Godot `4.3` is stored under `4.3.stable`. A future .NET installation would use a distinct identifier such as `4.3.stable.mono`.

Floating selectors are resolved each time a wrapper starts. If a newer matching stable version has been published, it is installed alongside earlier versions rather than replacing them.

## Volumes

Mount the advertised binary and template locations to reuse downloads across containers.

On Linux:

```yaml
services:
  godot:
    image: faulo/godot:latest
    init: true
    gpus: all
    environment:
      GODOT_VERSION: "4.6"
      BLENDER_VERSION: "5"
      NVIDIA_DRIVER_CAPABILITIES: all
    volumes:
      - godot-binaries:/godot/binaries
      - godot-templates:/godot/export_templates
      - blender:/blender
```

On Windows:

```yaml
services:
  godot:
    image: faulo/godot:latest
    devices:
      - "class/5B45201D-F2F2-4F3B-85BB-30FF1F953599"
    environment:
      GODOT_VERSION: "4.6"
      BLENDER_VERSION: "5"
    volumes:
      - godot-binaries:C:/godot/binaries
      - godot-templates:C:/godot/export_templates
      - blender:C:/blender
```

Godot's standard export-template directory is linked to `/godot/export_templates` on Linux and `C:/godot/export_templates` on Windows. Each installed Godot version has its own binary and export-template directory named with the exact template identifier. Each Blender version is placed in its own directory named with its full semantic version.

Cleanup of unused versions is outside the scope of this image.

The Compose examples also forward the host GPU. On Linux, the image includes the
Vulkan loader and `NVIDIA_DRIVER_CAPABILITIES=all` exposes the graphics driver in
addition to CUDA. On Windows containers, the forwarded device is available to
DirectX workloads; Godot must use its D3D12 rendering driver to render with it.

GPU passthrough does not by itself accelerate headless project import or export.
Godot's `--headless` display driver disables rendering, and Godot invokes Blender
in background mode to export `.blend` files to glTF. That Blender export path is
CPU-bound. Blender rendering or other explicitly GPU-backed tasks can use the
forwarded device when the host and container runtime support their graphics or
compute API.

## Local Build and Test

The local setup expects explicitly named `linux` and `windows` Docker contexts. Both builds use the repository root as their build context:

```text
docker --context linux build --pull --tag tmp/godot:latest --file linux/Dockerfile .
docker --context windows build --pull --tag tmp/godot:latest --file windows/Dockerfile .
```

Only images under the disposable `tmp/` namespace are used by the batch scripts. The platform-specific Explorer entry points are:

```text
docker-build-linux.bat
docker-build-windows.bat
docker-test-linux.bat
docker-test-windows.bat
```

The test configuration in `.env` installs the latest stable Godot 4 and Blender 4 releases into named volumes, then runs `godot --version`.
