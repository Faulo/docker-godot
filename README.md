# Godot Docker Image

This repository builds Linux and Windows Docker images for a Godot image that auto-installs Godot and Blender (optinal) on startup based on environment variables.

## Image Contents

Both image variants provide:

- TODOD

## Configuration

On startup, the docker health probe uses these environment variables to set up the image:

- `GODOT_VERSION` selects the binary and export templates of Godot to install.
- `BLENDER_VERSION`, if set to a semantic version string, selects the Blender binary to install.

The container also proves thin wrappers called `godot` and `blender` that forward their parameters to the proper executable, based on those environment variables.

## Versioning

For both `GODOT_VERSION` and `BLENDER_VERSION`, this image considers them as tho they came with the semver selector `~`, that is, it installs the latest version that still matches the version string given.

For example, at the time of writing, specifying `BLENDER_VERSION=4.4` would install Blender `4.4.3`. To install `4.4.0` exactly, specify `BLENDER_VERSION=4.4.0`. Note this differs from how Godot itself advertises its versions, where Godot `4.1` is `4.1.0`, whereas specifying `GODOT_VERSION=4.1` would install the latest available `4.1.x` (which may or may not be `4.1.0`.

## Volumes

To reuse the downloaded executables across sessions, the image advertises its mount points via `VOLUME`. In particular, these folders should be mounted:

On Linux:
```yaml
services:
  godot:
    image: faulo/godot:latest
    init: true
    gpus: all
    environment:
      GODOT_VERSION: 4.6
      BLENDER_VERSION: 5
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
      GODOT_VERSION: 4.6
      BLENDER_VERSION: 5
    volumes:
      - godot-binaries:C:/godot/binaries
      - godot-templates:C:/godot/export_templates
      - blender:/blender
```

The image sets up Godot's configuration to not use the default locations of `/root/.local/share/godot/export_templates` and `C:/Users/ContainerAdministrator/AppData/Roaming/Godot/export_templates` to instead use `/godot/export_templates` and `C:/godot/export_templates`, respectively.

Inside `godot/binaries` and `blender`, each installed version is placed in its own directory with a name matching its full version string (again noting Godot's `4` is treated as `4.0.0` here). Cleanup of unused versions is outside the scope of this image.

Also note the docker-compose example above forwards the GPU to the container, which can speed up graphics processing when available.