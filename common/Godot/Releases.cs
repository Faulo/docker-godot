using System;

namespace Godot;

sealed record GodotRelease(Version version, string tag);

sealed record BlenderRelease(Version version, string series);