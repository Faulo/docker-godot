using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace DockerGodot;

sealed class RuntimeSetup {
    static readonly TimeSpan LockTimeout = TimeSpan.FromHours(2);

    readonly PlatformInfo _platform;
    readonly IReleaseResolver _releaseResolver;
    readonly IDownloadClient _downloadClient;

    public RuntimeSetup(PlatformInfo platform, IReleaseResolver releaseResolver, IDownloadClient downloadClient) {
        _platform = platform;
        _releaseResolver = releaseResolver;
        _downloadClient = downloadClient;
    }

    public string PrepareGodot() {
        var godotSelector = RequireSelector(EnvironmentVariableNames.GODOT_VERSION);
        _platform.EnsureTemplateLink();
        ClearReady();

        var readyPaths = new List<string>();
        string? blender = null;
        string? blenderValue = Environment.GetEnvironmentVariable(EnvironmentVariableNames.BLENDER_VERSION);
        if (!string.IsNullOrWhiteSpace(blenderValue)) {
            var blenderSelector = VersionSelector.Parse(EnvironmentVariableNames.BLENDER_VERSION, blenderValue);
            blender = GetOrInstallBlender(blenderSelector);
            readyPaths.Add(blender);
        }

        string[] cached = ReadLinesIfPresent(Path.Combine(_platform.stateRoot, "godot"));
        string godot;
        if (CanUseCache(godotSelector, cached, 2)) {
            godot = cached[1];
        } else {
            using (AcquireLock(_platform.godotRoot))
            using (AcquireLock(_platform.templateRoot)) {
                godot = InstallGodot(godotSelector);
            }
            WriteGodotCache(godotSelector, godot);
        }

        if (blender != null) {
            GodotEditorSettings.Configure(_platform, godot, blender);
        }

        readyPaths.Add(godot);
        WriteReady(readyPaths);
        return godot;
    }

    public bool IsHealthy() {
        string ready = Path.Combine(_platform.stateRoot, "ready");
        string[] paths = ReadLinesIfPresent(ready);
        return paths.Length > 0 && paths.All(File.Exists);
    }

    string GetOrInstallBlender(VersionSelector selector) {
        string[] cached = ReadLinesIfPresent(Path.Combine(_platform.stateRoot, "blender"));
        string blender;
        if (CanUseCache(selector, cached, 1)) {
            blender = cached[1];
        } else {
            using (AcquireLock(_platform.blenderRoot)) {
                blender = InstallBlender(selector);
            }
            WriteBlenderCache(selector, blender);
        }

        return blender;
    }

    string InstallGodot(VersionSelector selector) {
        var release = _releaseResolver.ResolveGodot(selector);
        string installId = release.tag.Replace('-', '.');
        string installDirectory = Path.Combine(_platform.godotRoot, installId);
        string templateDirectory = Path.Combine(_platform.templateRoot, installId);
        string executable = Path.Combine(installDirectory, _platform.GodotExecutable(release.tag));

        if (!File.Exists(executable)) {
            DeleteIncompleteInstallation(installDirectory, _platform.godotRoot);
            InstallGodotEditor(release, installId, installDirectory);
        }

        string templateMarker = Path.Combine(templateDirectory, "version.txt");
        if (!File.Exists(templateMarker)) {
            DeleteIncompleteInstallation(templateDirectory, _platform.templateRoot);
            InstallGodotTemplates(release, installId, templateDirectory);
        }

        return executable;
    }

    void InstallGodotEditor(GodotRelease release, string installId, string installDirectory) {
        Log("installing Godot " + release.version + " (" + installId + ")");
        CleanupTemporaryDirectories(_platform.godotRoot, installId);
        string temporary = CreateTemporaryDirectory(_platform.godotRoot, installId);
        try {
            string filename = _platform.GodotArchive(release.tag);
            string archive = Path.Combine(temporary, filename);
            string sums = Path.Combine(temporary, "SHA512-SUMS.txt");
            string baseUrl = "https://github.com/godotengine/godot-builds/releases/download/" + release.tag;
            _downloadClient.Save(baseUrl + "/" + filename, archive);
            _downloadClient.Save(baseUrl + "/SHA512-SUMS.txt", sums);
            ChecksumVerifier.VerifySha512(archive, sums);
            string unpacked = Path.Combine(temporary, "unpacked");
            ZipFile.ExtractToDirectory(archive, unpacked);
            string unpackedExecutable = Path.Combine(unpacked, _platform.GodotExecutable(release.tag));
            if (!File.Exists(unpackedExecutable)) {
                throw new InvalidDataException("Godot archive has an unexpected layout");
            }
            if (!_platform.isWindows) {
                ProcessRunner.Run("chmod", new[] { "+x", unpackedExecutable }, true);
            }
            Directory.Move(unpacked, installDirectory);
        } finally {
            DeleteDirectory(temporary);
        }
    }

    void InstallGodotTemplates(GodotRelease release, string installId, string templateDirectory) {
        Log("installing Godot export templates " + installId);
        CleanupTemporaryDirectories(_platform.templateRoot, installId);
        string temporary = CreateTemporaryDirectory(_platform.templateRoot, installId);
        try {
            string filename = "Godot_v" + release.tag + "_export_templates.tpz";
            string archive = Path.Combine(temporary, filename);
            string sums = Path.Combine(temporary, "SHA512-SUMS.txt");
            string baseUrl = "https://github.com/godotengine/godot-builds/releases/download/" + release.tag;
            _downloadClient.Save(baseUrl + "/" + filename, archive);
            _downloadClient.Save(baseUrl + "/SHA512-SUMS.txt", sums);
            ChecksumVerifier.VerifySha512(archive, sums);
            string unpacked = Path.Combine(temporary, "unpacked");
            ZipFile.ExtractToDirectory(archive, unpacked);
            string templates = Path.Combine(unpacked, "templates");
            if (!Directory.Exists(templates)) {
                throw new InvalidDataException("Godot template archive has an unexpected layout");
            }
            Directory.Move(templates, templateDirectory);
        } finally {
            DeleteDirectory(temporary);
        }
    }

    string InstallBlender(VersionSelector selector) {
        var release = _releaseResolver.ResolveBlender(selector, _platform);
        string version = release.version.ToString(3);
        string installDirectory = Path.Combine(_platform.blenderRoot, version);
        string executable = Path.Combine(installDirectory, _platform.blenderExecutable);
        if (File.Exists(executable)) {
            return executable;
        }

        DeleteIncompleteInstallation(installDirectory, _platform.blenderRoot);
        Log("installing Blender " + version);
        CleanupTemporaryDirectories(_platform.blenderRoot, version);
        string temporary = CreateTemporaryDirectory(_platform.blenderRoot, version);
        try {
            string filename = _platform.BlenderArchive(version);
            string archive = Path.Combine(temporary, filename);
            string sums = Path.Combine(temporary, "blender-" + version + ".sha256");
            string baseUrl = "https://download.blender.org/release/" + release.series;
            _downloadClient.Save(baseUrl + "/" + filename, archive);
            _downloadClient.Save(baseUrl + "/blender-" + version + ".sha256", sums);
            ChecksumVerifier.VerifySha256(archive, sums);
            string unpacked = Path.Combine(temporary, "unpacked");
            Directory.CreateDirectory(unpacked);
            if (_platform.isWindows) {
                ZipFile.ExtractToDirectory(archive, unpacked);
                string? source = Directory.GetDirectories(unpacked).FirstOrDefault();
                if (source == null || !File.Exists(Path.Combine(source, _platform.blenderExecutable))) {
                    throw new InvalidDataException("Blender archive has an unexpected layout");
                }
                Directory.Move(source, installDirectory);
            } else {
                ProcessRunner.Run("tar", new[] { "-xJf", archive, "-C", unpacked, "--strip-components=1" }, true);
                if (!File.Exists(Path.Combine(unpacked, _platform.blenderExecutable))) {
                    throw new InvalidDataException("Blender archive has an unexpected layout");
                }
                Directory.Move(unpacked, installDirectory);
            }
        } finally {
            DeleteDirectory(temporary);
        }

        return executable;
    }

    static VersionSelector RequireSelector(string name) {
        return VersionSelector.Parse(name, Environment.GetEnvironmentVariable(name) ?? string.Empty);
    }

    internal static bool CanUseCache(VersionSelector selector, string[] cached, int pathCount) {
        return selector.ComponentCount() == 3
            && cached.Length == pathCount + 1
            && cached[0] == selector.ToString()
            && cached.Skip(1).All(File.Exists);
    }

    static FileStream AcquireLock(string root) {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, ".docker-godot.lock");
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < LockTimeout) {
            try {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) {
                Thread.Sleep(250);
            }
        }

        throw new TimeoutException("timed out waiting for installation lock: " + path);
    }

    static string CreateTemporaryDirectory(string root, string installId) {
        string path = Path.Combine(root, "." + installId + "." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void CleanupTemporaryDirectories(string root, string installId) {
        foreach (string path in Directory.GetDirectories(root, "." + installId + ".*")) {
            DeleteDirectory(path);
        }
    }

    static void DeleteIncompleteInstallation(string path, string expectedRoot) {
        if (!Directory.Exists(path)) {
            return;
        }

        string root = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
            throw new IOException("refusing to remove installation outside its managed root: " + candidate);
        }

        Log("removing incomplete installation " + candidate);
        Directory.Delete(candidate, true);
    }

    static void DeleteDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, true);
        }
    }

    void WriteBlenderCache(VersionSelector selector, string executable) {
        Directory.CreateDirectory(_platform.stateRoot);
        File.WriteAllLines(Path.Combine(_platform.stateRoot, "blender"), new[] { selector.ToString(), executable });
    }

    void WriteGodotCache(VersionSelector selector, string executable) {
        string installId = Path.GetFileName(Path.GetDirectoryName(executable)!);
        string templateMarker = Path.Combine(_platform.templateRoot, installId, "version.txt");
        Directory.CreateDirectory(_platform.stateRoot);
        File.WriteAllLines(Path.Combine(_platform.stateRoot, "godot"), new[] { selector.ToString(), executable, templateMarker });
    }

    void ClearReady() {
        string ready = Path.Combine(_platform.stateRoot, "ready");
        if (File.Exists(ready)) {
            File.Delete(ready);
        }
    }

    void WriteReady(IEnumerable<string> paths) {
        Directory.CreateDirectory(_platform.stateRoot);
        string ready = Path.Combine(_platform.stateRoot, "ready");
        string temporary = ready + "." + Guid.NewGuid().ToString("N");
        File.WriteAllLines(temporary, paths);
        File.Move(temporary, ready, true);
    }

    static string[] ReadLinesIfPresent(string path) {
        return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
    }

    static void Log(string message) {
        Console.Out.WriteLine("docker-godot: " + message);
    }
}
