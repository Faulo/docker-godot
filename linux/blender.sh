#!/bin/sh
set -eu

executable="$(/usr/local/lib/docker-godot/setup blender)"
exec "$executable" "$@"
