using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

static class GodotEditorSettings {
    static readonly Regex blenderSetting = new("^filesystem/import/blender/blender_path = [^\\r\\n]*", RegexOptions.Multiline);

    public static void Configure(PlatformInfo platform, string godotExecutable, string blenderExecutable) {
        string installId = new DirectoryInfo(Path.GetDirectoryName(godotExecutable)!).Name;
        string[] parts = installId.Split('.');
        int major = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int minor = int.Parse(parts[1], CultureInfo.InvariantCulture);
        string settingsFile = FindSettingsFile(platform.godotSettingsRoot, major, minor);

        Directory.CreateDirectory(platform.godotSettingsRoot);
        string blenderPath = platform.isWindows ? blenderExecutable.Replace('\\', '/') : blenderExecutable;
        string setting = "filesystem/import/blender/blender_path = \"" + blenderPath.Replace("\"", "\\\"") + "\"";
        string? contents = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        File.WriteAllText(settingsFile, UpdateContents(contents, setting), new UTF8Encoding(false));
    }

    internal static string UpdateContents(string? contents, string setting) {
        if (contents == null) {
            return "[gd_resource type=\"EditorSettings\" format=3]" + Environment.NewLine + Environment.NewLine
                + "[resource]" + Environment.NewLine + setting + Environment.NewLine;
        }

        return blenderSetting.IsMatch(contents)
            ? blenderSetting.Replace(contents, setting)
            : contents.TrimEnd() + Environment.NewLine + setting + Environment.NewLine;
    }

    static string FindSettingsFile(string settingsRoot, int major, int minor) {
        for (int candidateMinor = minor; candidateMinor >= 3; candidateMinor--) {
            string candidate = Path.Combine(settingsRoot, "editor_settings-" + major + "." + candidateMinor + ".tres");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        string legacy = Path.Combine(settingsRoot, "editor_settings-" + major + ".tres");
        if (minor < 3 || File.Exists(legacy)) {
            return legacy;
        }

        return Path.Combine(settingsRoot, "editor_settings-" + major + "." + minor + ".tres");
    }
}
