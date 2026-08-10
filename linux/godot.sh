#!/bin/sh
set -eu

executable="$(/usr/local/lib/docker-godot/setup godot)"
exec "$executable" "$@"
