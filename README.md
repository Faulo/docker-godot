# Godot for Linux and Windows containers

[![Docker](https://github.com/Faulo/docker-godot/actions/workflows/docker.yml/badge.svg?branch=main)](https://github.com/Faulo/docker-godot/actions/workflows/docker.yml) [![Docker image updated](https://img.shields.io/docker/last-updated/faulo/godot/latest)](https://hub.docker.com/r/faulo/godot/tags?name=latest) [![License: MIT](https://img.shields.io/github/license/Faulo/docker-godot)](LICENSE) [![Platforms: Linux and Windows amd64](https://img.shields.io/badge/platforms-Linux%20%7C%20Windows%20amd64-0078D4)](#available-images)

[`faulo/godot`](https://hub.docker.com/r/faulo/godot) installs and runs stable Godot releases, matching export templates, and optional Blender versions in Linux and Windows containers. Reuse downloads through named volumes and select the release series required by each project.

## Available images

| Tag | Platform | Runtime base |
| --- | --- | --- |
| `latest` | Linux amd64 and Windows amd64 | Debian Trixie Slim; full Windows LTSC 2019 |
| `latest-linux`, `latest-linux-amd64` | Linux amd64 | Debian Trixie Slim |
| `latest-windows`, `latest-windows-ltsc2019` | Windows amd64 | Full Windows LTSC 2019 through `faulo/choco` |

Use `latest` for automatic platform selection, or an explicit platform tag when
needed. Windows containers require a compatible Windows host. Tags receive
updates in place; pin a digest when you require an exact image.

## Quick start

Run a standard Godot release:

```text
docker run --rm --env GODOT_VERSION=4 faulo/godot:latest godot --version
```

The first invocation downloads the selected editor and matching export
templates. Mount the volumes described below to reuse them across containers.

## Included tools

Both image variants provide:

- The standard Godot editor and matching export templates selected by `GODOT_VERSION`.
- Blender selected by `BLENDER_VERSION`, when configured.
- A `godot` launcher that installs and starts the selected versions and reports readiness to the health check.

The Windows variant includes Chocolatey, Visual C++ prerequisites, container
compatibility fixes, and PowerShell 7 through the shared
[`faulo/choco` base](https://github.com/Faulo/docker-choco).
The Linux variant uses Debian Trixie Slim.

The launcher supports standard Godot builds. Godot .NET/Mono builds are not supported.

## Running Godot

The image has no entrypoint and defaults to `godot help`. An explicit Docker
command replaces that default. Invoke `godot` explicitly when you want the
launcher; other commands can run directly, including through `docker exec`.

The working directory is `/godot` on Linux and `C:/godot` on Windows. The image-owned `godot` executable lives there, and that directory is prepended to `PATH`.

The `godot` launcher performs the following work before starting the selected Godot executable:

1. If `BLENDER_VERSION` is set, resolve and install Blender.
2. Resolve and install Godot and its matching export templates.
3. Publish the readiness state consumed by the Docker health check.
4. Start Godot with the original arguments and return its exit code.

Godot imports `.blend` files by invoking an executable configured in its editor settings. When Blender is selected, the `godot` launcher preserves the other editor settings and points `filesystem/import/blender/blender_path` at the resolved versioned Blender executable before Godot starts. This lets Godot manage Blender's persistent import process directly.

Installations use a lock in each shared volume, resume interrupted downloads into temporary directories, verify the publisher-provided SHA-512 or SHA-256 checksum, and publish a completed version directory atomically. This permits concurrent containers to share the same named volumes safely. Version resolution, locking, downloads, verification, readiness, editor configuration, and process launching share one C# implementation across Linux and Windows; only platform paths, artifact names, and archive extraction differ.

The Docker health check only reports launcher readiness; it never performs installation. The launcher publishes readiness immediately before the requested Godot process starts, and Docker reports the container as `healthy` when the next probe observes it. Short-lived commands may exit before Docker runs its first health probe, while still returning the launched command's exit status normally.

## Configuration

The launcher uses these environment variables:

- `GODOT_VERSION` is required by `godot` and selects the standard Godot editor and export templates.
- `BLENDER_VERSION` is optional; when set, Blender is installed before Godot starts.

The launcher rejects `--import` when the selected working directory does not
contain a `project.godot` file. This matches the command's project
requirement and avoids a native Godot process that otherwise remains running
after reporting the missing project. An explicit `--path` selects the directory
that is checked.

Only stable releases are considered. A selector that cannot be resolved to a stable release is an error, as is an invalid selector.

## Version Selectors

Selectors contain one to three numeric components and use prefix-compatible semantics:

- `4` selects the latest stable `4.x.y` release.
- `4.3` selects the latest stable `4.3.x` release.
- `4.3.0` selects exactly `4.3.0`.

Godot itself labels a release without a patch component, such as `4.3-stable`, where semantic versioning would normally write `4.3.0`. The selector still treats `GODOT_VERSION=4.3` as the complete `4.3.x` series and `GODOT_VERSION=4` as the complete `4.x.y` series.

Godot installations use the exact version identifier expected by their export templates. For example, standard Godot `4.3` is stored under `4.3.stable`.

Floating selectors are resolved each time the launcher starts. If a newer matching stable version has been published, it is installed alongside earlier versions rather than replacing them.

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

volumes:
  godot-binaries:
  godot-templates:
  blender:
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

volumes:
  godot-binaries:
  godot-templates:
  blender:
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

## Maintenance and updates

Linux amd64 and full Windows LTSC 2019 images are rebuilt monthly to pick up
base-image and dependency updates. Use `docker build --pull` when rebuilding a
downstream image, or recreate containers with a newly pulled image.

Godot and Blender version selection happens at runtime. Floating selectors
adopt newer matching stable releases on the next launcher invocation; exact
selectors keep the requested release. Existing downloads remain in their
versioned directories until you remove them.

## Tests and development

Run the launcher unit tests from the repository root with the .NET 9 SDK:

```text
dotnet test common/Godot.Tests/Godot.Tests.csproj
```

The unit suite requires no Docker engine. It covers version selection,
release-list parsing, checksum verification, editor settings, argument
forwarding, platform artifacts, and launcher readiness.

The repository also includes image integration tests for supported stable Godot
series, missing-project errors, fixture imports, and Windows executable export.
They require Pester and a compatible container engine.

Both Dockerfiles use the repository root as their build context. Run the
appropriate command on an engine configured for the selected container OS:

```text
docker build --pull --tag tmp/godot:latest --file linux/Dockerfile .
docker build --pull --tag tmp/godot:latest --file windows/Dockerfile .
```

## Support and contributions

[Open an issue](https://github.com/Faulo/docker-godot/issues) with the image tag
or digest, Godot and Blender selectors, container OS, and a minimal project or
command that reproduces the problem. For Windows, include the host version and
isolation mode. Remove credentials and private project data from logs.

Pull requests are welcome. Include tests for launcher behavior changes and
update the documentation when public options change.

## License

The original code, Dockerfiles, scripts, and documentation are licensed under
the [MIT License](LICENSE). Godot, Blender, and other bundled or downloaded
software retain their respective licenses. Windows base-image licensing
continues to apply to the Windows variant.
