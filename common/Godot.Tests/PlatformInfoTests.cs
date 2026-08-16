using DockerGodot;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class PlatformInfoTests {
    [Test]
    public void GeneratesLinuxArtifactNames() {
        var platform = CreatePlatform(false);

        Assert.That(platform.GodotArchive("4.3-stable"), Is.EqualTo("Godot_v4.3-stable_linux.x86_64.zip"));
        Assert.That(platform.GodotExecutable("4.3-stable"), Is.EqualTo("Godot_v4.3-stable_linux.x86_64"));
        Assert.That(platform.BlenderArchive("4.2.1"), Is.EqualTo("blender-4.2.1-linux-x64.tar.xz"));
    }

    [Test]
    public void GeneratesWindowsArtifactNames() {
        var platform = CreatePlatform(true);

        Assert.That(platform.GodotArchive("4.3-stable"), Is.EqualTo("Godot_v4.3-stable_win64.exe.zip"));
        Assert.That(platform.GodotExecutable("4.3-stable"), Is.EqualTo("Godot_v4.3-stable_win64_console.exe"));
        Assert.That(platform.BlenderArchive("4.2.1"), Is.EqualTo("blender-4.2.1-windows-x64.zip"));
    }

    static PlatformInfo CreatePlatform(bool windows) {
        return new PlatformInfo(windows, "godot", "templates", "blender", "state", "data", "settings");
    }
}