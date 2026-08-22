using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Godot;

sealed class PlatformInfo {
    public static readonly PlatformInfo current = CreateCurrent();

    internal PlatformInfo(
        bool isWindows,
        string godotRoot,
        string templateRoot,
        string blenderRoot,
        string stateRoot,
        string godotDataRoot,
        string godotSettingsRoot) {
        this.isWindows = isWindows;
        this.godotRoot = godotRoot;
        this.templateRoot = templateRoot;
        this.blenderRoot = blenderRoot;
        this.stateRoot = stateRoot;
        this.godotDataRoot = godotDataRoot;
        this.godotSettingsRoot = godotSettingsRoot;
        blenderExecutable = isWindows ? "blender.exe" : "blender";
    }

    public bool isWindows { get; }
    public string godotRoot { get; }
    public string templateRoot { get; }
    public string blenderRoot { get; }
    public string stateRoot { get; }
    public string blenderExecutable { get; }
    public string godotDataRoot { get; }
    public string godotSettingsRoot { get; }

    public string GodotArchive(string tag) => isWindows ? "Godot_v" + tag + "_win64.exe.zip" : "Godot_v" + tag + "_linux.x86_64.zip";

    public string GodotExecutable(string tag) => isWindows ? "Godot_v" + tag + "_win64_console.exe" : "Godot_v" + tag + "_linux.x86_64";

    public string BlenderArchive(string version) => isWindows ? "blender-" + version + "-windows-x64.zip" : "blender-" + version + "-linux-x64.tar.xz";

    public string BlenderListingPattern(string major, string minor) {
        string suffix = isWindows ? @"windows-x64\.zip" : @"linux-x64\.tar\.xz";
        return "blender-(" + Regex.Escape(major) + "\\." + Regex.Escape(minor) + "\\.[0-9]+)-" + suffix;
    }

    public void EnsureTemplateLink() {
        string link = Path.Combine(godotDataRoot, "export_templates");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (Directory.Exists(link)) {
            var attributes = File.GetAttributes(link);
            if ((attributes & FileAttributes.ReparsePoint) == 0 && Directory.EnumerateFileSystemEntries(link).Any()) {
                throw new IOException("Godot export template path exists and is not an image-managed link: " + link);
            }

            Directory.Delete(link, false);
        }

        if (isWindows) {
            ProcessRunner.Run("cmd.exe", ["/d", "/c", "mklink", "/J", link, templateRoot], true);
        } else {
            Directory.CreateSymbolicLink(link, templateRoot);
        }
    }

    static PlatformInfo CreateCurrent() {
        bool isWindows = OperatingSystem.IsWindows();
        string home = Environment.GetEnvironmentVariable(isWindows ? "USERPROFILE" : "HOME") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(home)) {
            home = isWindows ? @"C:\Users\ContainerAdministrator" : "/root";
        }

        string config = Environment.GetEnvironmentVariable(isWindows ? "APPDATA" : "XDG_CONFIG_HOME") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(config)) {
            config = isWindows ? Path.Combine(home, "AppData", "Roaming") : Path.Combine(home, ".config");
        }

        string data = Environment.GetEnvironmentVariable(isWindows ? "APPDATA" : "XDG_DATA_HOME") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(data)) {
            data = isWindows ? Path.Combine(home, "AppData", "Roaming") : Path.Combine(home, ".local", "share");
        }

        return new PlatformInfo(
            isWindows,
            isWindows ? @"C:\godot\binaries" : "/godot/binaries",
            isWindows ? @"C:\godot\export_templates" : "/godot/export_templates",
            isWindows ? @"C:\blender" : "/blender",
            isWindows ? @"C:\run\docker-godot" : "/run/docker-godot",
            Path.Combine(data, isWindows ? "Godot" : "godot"),
            Path.Combine(config, isWindows ? "Godot" : "godot"));
    }
}