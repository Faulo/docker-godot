using Xunit;

public sealed class PlatformInfoTests {
    [Fact]
    public void GeneratesLinuxArtifactNames() {
        var platform = CreatePlatform(false);

        Assert.Equal("Godot_v4.3-stable_linux.x86_64.zip", platform.GodotArchive("4.3-stable"));
        Assert.Equal("Godot_v4.3-stable_linux.x86_64", platform.GodotExecutable("4.3-stable"));
        Assert.Equal("blender-4.2.1-linux-x64.tar.xz", platform.BlenderArchive("4.2.1"));
    }

    [Fact]
    public void GeneratesWindowsArtifactNames() {
        var platform = CreatePlatform(true);

        Assert.Equal("Godot_v4.3-stable_win64.exe.zip", platform.GodotArchive("4.3-stable"));
        Assert.Equal("Godot_v4.3-stable_win64_console.exe", platform.GodotExecutable("4.3-stable"));
        Assert.Equal("blender-4.2.1-windows-x64.zip", platform.BlenderArchive("4.2.1"));
    }

    static PlatformInfo CreatePlatform(bool windows) {
        return new PlatformInfo(windows, "godot", "templates", "blender", "state", "data", "settings");
    }
}
