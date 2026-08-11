using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string invokedAs = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]).ToLowerInvariant();
            string operation;
            string[] processArguments;
            if (invokedAs == "godot" || invokedAs == "blender")
            {
                operation = invokedAs;
                processArguments = arguments;
            }
            else
            {
                if (arguments.Length == 0)
                {
                    throw new InvalidOperationException("usage: docker-godot godot|blender|health");
                }
                operation = arguments[0].ToLowerInvariant();
                processArguments = arguments.Skip(1).ToArray();
            }

            RuntimeSetup setup = new RuntimeSetup(PlatformInfo.Current);
            if (operation == "health")
            {
                return setup.IsHealthy() ? 0 : 1;
            }

            string executable;
            if (operation == "godot")
            {
                executable = setup.PrepareGodot();
            }
            else if (operation == "blender")
            {
                executable = setup.PrepareBlender();
            }
            else
            {
                throw new InvalidOperationException("usage: docker-godot godot|blender|health");
            }

            return ProcessRunner.Run(executable, processArguments, false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("docker-godot: " + exception.Message);
            return 1;
        }
    }
}

internal sealed class RuntimeSetup
{
    private readonly PlatformInfo platform;

    public RuntimeSetup(PlatformInfo platform)
    {
        this.platform = platform;
    }

    public string PrepareGodot()
    {
        string godotSelector = RequireSelector("GODOT_VERSION");
        platform.EnsureTemplateLink();
        List<string> readyPaths = new List<string>();
        string blender = null;
        string blenderSelector = Environment.GetEnvironmentVariable("BLENDER_VERSION");
        if (!string.IsNullOrWhiteSpace(blenderSelector))
        {
            blender = GetOrInstallBlender(blenderSelector);
            readyPaths.Add(blender);
        }

        string[] cached = ReadLinesIfPresent(Path.Combine(platform.StateRoot, "godot"));
        string godot;
        if (cached.Length == 3 && cached[0] == godotSelector && File.Exists(cached[1]) && File.Exists(cached[2]))
        {
            godot = cached[1];
        }
        else
        {
            using (AcquireLock(platform.GodotRoot))
            using (AcquireLock(platform.TemplateRoot))
            {
                godot = InstallGodot(godotSelector);
            }
            WriteGodotCache(godotSelector, godot);
        }

        if (readyPaths.Count > 0)
        {
            ConfigureBlenderForGodot(godot, blender);
        }
        readyPaths.Add(godot);
        WriteReady(readyPaths);
        return godot;
    }

    public string PrepareBlender()
    {
        string selector = RequireSelector("BLENDER_VERSION");
        string blender = GetOrInstallBlender(selector);
        WriteReady(new[] { blender });
        return blender;
    }

    private string GetOrInstallBlender(string selector)
    {
        string[] cached = ReadLinesIfPresent(Path.Combine(platform.StateRoot, "blender"));
        string blender;
        if (cached.Length == 2 && cached[0] == selector && File.Exists(cached[1]))
        {
            blender = cached[1];
        }
        else
        {
            blender = WithLock(platform.BlenderRoot, delegate { return InstallBlender(selector); });
            WriteBlenderCache(selector, blender);
        }
        return blender;
    }

    public bool IsHealthy()
    {
        string ready = Path.Combine(platform.StateRoot, "ready");
        string[] paths = ReadLinesIfPresent(ready);
        return paths.Length > 0 && paths.All(File.Exists);
    }

    private string InstallGodot(string selector)
    {
        ValidateSelector("GODOT_VERSION", selector);
        if (selector.Split('.')[0] != "4")
        {
            throw new InvalidOperationException("only standard Godot 4 releases are currently supported");
        }

        GodotRelease release = ReleaseResolver.ResolveGodot(selector);
        string installId = release.Tag.Replace('-', '.');
        string installDirectory = Path.Combine(platform.GodotRoot, installId);
        string templateDirectory = Path.Combine(platform.TemplateRoot, installId);
        string executable = Path.Combine(installDirectory, platform.GodotExecutable(release.Tag));

        if (!File.Exists(executable))
        {
            Log("installing Godot " + release.Version + " (" + installId + ")");
            CleanupTemporaryDirectories(platform.GodotRoot, installId);
            string temporary = CreateTemporaryDirectory(platform.GodotRoot, installId);
            try
            {
                string filename = platform.GodotArchive(release.Tag);
                string archive = Path.Combine(temporary, filename);
                string sums = Path.Combine(temporary, "SHA512-SUMS.txt");
                string baseUrl = "https://github.com/godotengine/godot-builds/releases/download/" + release.Tag;
                Download.Save(baseUrl + "/" + filename, archive);
                Download.Save(baseUrl + "/SHA512-SUMS.txt", sums);
                Checksum.Verify(archive, sums, "SHA512");
                string unpacked = Path.Combine(temporary, "unpacked");
                ZipFile.ExtractToDirectory(archive, unpacked);
                if (!platform.IsWindows)
                {
                    ProcessRunner.Run("chmod", new[] { "+x", Path.Combine(unpacked, platform.GodotExecutable(release.Tag)) }, true);
                }
                Directory.Move(unpacked, installDirectory);
            }
            finally
            {
                DeleteDirectory(temporary);
            }
        }

        string templateMarker = Path.Combine(templateDirectory, "version.txt");
        if (!File.Exists(templateMarker))
        {
            Log("installing Godot export templates " + installId);
            CleanupTemporaryDirectories(platform.TemplateRoot, installId);
            string temporary = CreateTemporaryDirectory(platform.TemplateRoot, installId);
            try
            {
                string filename = "Godot_v" + release.Tag + "_export_templates.tpz";
                string archive = Path.Combine(temporary, filename);
                string sums = Path.Combine(temporary, "SHA512-SUMS.txt");
                string baseUrl = "https://github.com/godotengine/godot-builds/releases/download/" + release.Tag;
                Download.Save(baseUrl + "/" + filename, archive);
                Download.Save(baseUrl + "/SHA512-SUMS.txt", sums);
                Checksum.Verify(archive, sums, "SHA512");
                string unpacked = Path.Combine(temporary, "unpacked");
                ZipFile.ExtractToDirectory(archive, unpacked);
                string templates = Path.Combine(unpacked, "templates");
                if (!Directory.Exists(templates))
                {
                    throw new InvalidDataException("Godot template archive has an unexpected layout");
                }
                Directory.Move(templates, templateDirectory);
            }
            finally
            {
                DeleteDirectory(temporary);
            }
        }

        return executable;
    }

    private string InstallBlender(string selector)
    {
        ValidateSelector("BLENDER_VERSION", selector);
        BlenderRelease release = ReleaseResolver.ResolveBlender(selector, platform);
        string version = release.Version.ToString(3);
        string installDirectory = Path.Combine(platform.BlenderRoot, version);
        string executable = Path.Combine(installDirectory, platform.BlenderExecutable);
        if (File.Exists(executable))
        {
            return executable;
        }

        Log("installing Blender " + version);
        CleanupTemporaryDirectories(platform.BlenderRoot, version);
        string temporary = CreateTemporaryDirectory(platform.BlenderRoot, version);
        try
        {
            string filename = platform.BlenderArchive(version);
            string archive = Path.Combine(temporary, filename);
            string sums = Path.Combine(temporary, "blender-" + version + ".sha256");
            string baseUrl = "https://download.blender.org/release/" + release.Series;
            Download.Save(baseUrl + "/" + filename, archive);
            Download.Save(baseUrl + "/blender-" + version + ".sha256", sums);
            Checksum.Verify(archive, sums, "SHA256");
            string unpacked = Path.Combine(temporary, "unpacked");
            Directory.CreateDirectory(unpacked);
            if (platform.IsWindows)
            {
                ZipFile.ExtractToDirectory(archive, unpacked);
                string source = Directory.GetDirectories(unpacked).FirstOrDefault();
                if (source == null || !File.Exists(Path.Combine(source, platform.BlenderExecutable)))
                {
                    throw new InvalidDataException("Blender archive has an unexpected layout");
                }
                Directory.Move(source, installDirectory);
            }
            else
            {
                ProcessRunner.Run("tar", new[] { "-xJf", archive, "-C", unpacked, "--strip-components=1" }, true);
                if (!File.Exists(Path.Combine(unpacked, platform.BlenderExecutable)))
                {
                    throw new InvalidDataException("Blender archive has an unexpected layout");
                }
                Directory.Move(unpacked, installDirectory);
            }
        }
        finally
        {
            DeleteDirectory(temporary);
        }
        return executable;
    }

    private void ConfigureBlenderForGodot(string godotExecutable, string blenderExecutable)
    {
        string installId = new DirectoryInfo(Path.GetDirectoryName(godotExecutable)).Name;
        string[] parts = installId.Split('.');
        int major = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int minor = int.Parse(parts[1], CultureInfo.InvariantCulture);
        string settingsRoot = platform.GodotSettingsRoot;
        string settingsFile = null;
        for (int candidateMinor = minor; candidateMinor >= 3; candidateMinor--)
        {
            string candidate = Path.Combine(settingsRoot, "editor_settings-" + major + "." + candidateMinor + ".tres");
            if (File.Exists(candidate))
            {
                settingsFile = candidate;
                break;
            }
        }
        string legacy = Path.Combine(settingsRoot, "editor_settings-" + major + ".tres");
        if (settingsFile == null && (minor < 3 || File.Exists(legacy)))
        {
            settingsFile = legacy;
        }
        if (settingsFile == null)
        {
            settingsFile = Path.Combine(settingsRoot, "editor_settings-" + major + "." + minor + ".tres");
        }

        Directory.CreateDirectory(settingsRoot);
        string blenderPath = platform.IsWindows ? blenderExecutable.Replace('\\', '/') : blenderExecutable;
        string setting = "filesystem/import/blender/blender_path = \"" + blenderPath + "\"";
        string contents;
        if (File.Exists(settingsFile))
        {
            contents = File.ReadAllText(settingsFile);
            Regex line = new Regex("^filesystem/import/blender/blender_path = .*$", RegexOptions.Multiline);
            contents = line.IsMatch(contents)
                ? line.Replace(contents, setting)
                : contents.TrimEnd() + Environment.NewLine + setting + Environment.NewLine;
        }
        else
        {
            contents = "[gd_resource type=\"EditorSettings\" format=3]" + Environment.NewLine + Environment.NewLine
                + "[resource]" + Environment.NewLine + setting + Environment.NewLine;
        }
        File.WriteAllText(settingsFile, contents, new UTF8Encoding(false));
    }

    private static string RequireSelector(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(name + " is required");
        }
        return value;
    }

    private static void ValidateSelector(string name, string value)
    {
        if (!Regex.IsMatch(value, "^[0-9]+(\\.[0-9]+){0,2}$"))
        {
            throw new InvalidOperationException(name + " must contain one to three numeric components, got: " + value);
        }
    }

    private static string WithLock(string root, Func<string> action)
    {
        using (AcquireLock(root))
        {
            return action();
        }
    }

    private static FileStream AcquireLock(string root)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, ".docker-godot.lock");
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static string CreateTemporaryDirectory(string root, string installId)
    {
        string path = Path.Combine(root, "." + installId + "." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupTemporaryDirectories(string root, string installId)
    {
        foreach (string path in Directory.GetDirectories(root, "." + installId + ".*"))
        {
            DeleteDirectory(path);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private void WriteBlenderCache(string selector, string executable)
    {
        Directory.CreateDirectory(platform.StateRoot);
        File.WriteAllLines(Path.Combine(platform.StateRoot, "blender"), new[] { selector, executable });
    }

    private void WriteGodotCache(string selector, string executable)
    {
        string installId = Path.GetFileName(Path.GetDirectoryName(executable));
        string templateMarker = Path.Combine(platform.TemplateRoot, installId, "version.txt");
        Directory.CreateDirectory(platform.StateRoot);
        File.WriteAllLines(Path.Combine(platform.StateRoot, "godot"), new[] { selector, executable, templateMarker });
    }

    private void WriteReady(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(platform.StateRoot);
        string ready = Path.Combine(platform.StateRoot, "ready");
        string temporary = ready + "." + Guid.NewGuid().ToString("N");
        File.WriteAllLines(temporary, paths);
        if (File.Exists(ready))
        {
            File.Delete(ready);
        }
        File.Move(temporary, ready);
    }

    private static string[] ReadLinesIfPresent(string path)
    {
        return File.Exists(path) ? File.ReadAllLines(path) : new string[0];
    }

    private static void Log(string message)
    {
        Console.Out.WriteLine("docker-godot: " + message);
    }
}

internal static class ReleaseResolver
{
    public static GodotRelease ResolveGodot(string selector)
    {
        List<GodotRelease> candidates = new List<GodotRelease>();
        Regex stable = new Regex("^(\\d+)\\.(\\d+)(?:\\.(\\d+))?-stable$");
        for (int page = 1; page <= 10; page++)
        {
            string uri = "https://api.github.com/repos/godotengine/godot-builds/releases?per_page=100&page=" + page;
            string response = Download.ReadText(uri, true);
            MatchCollection tags = Regex.Matches(response, "\\\"tag_name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
            foreach (Match tagMatch in tags)
            {
                string tag = tagMatch.Groups[1].Value;
                Match match = stable.Match(tag);
                if (!match.Success)
                {
                    continue;
                }
                int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                Version version = new Version(
                    int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    patch);
                if (Matches(version, selector))
                {
                    candidates.Add(new GodotRelease(version, tag));
                }
            }
            if (candidates.Count > 0 || tags.Count < 100)
            {
                break;
            }
        }
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("no stable Godot release matches GODOT_VERSION=" + selector);
        }
        return candidates.OrderBy(candidate => candidate.Version).Last();
    }

    public static BlenderRelease ResolveBlender(string selector, PlatformInfo platform)
    {
        string[] selectorParts = selector.Split('.');
        string major = selectorParts[0];
        string root = Download.ReadText("https://download.blender.org/release/", false);
        MatchCollection seriesMatches = Regex.Matches(root, "Blender" + Regex.Escape(major) + "\\.(\\d+)/");
        List<BlenderRelease> candidates = new List<BlenderRelease>();
        foreach (string series in seriesMatches.Cast<Match>().Select(match => match.Value).Distinct())
        {
            Match minorMatch = Regex.Match(series, "^Blender\\d+\\.(\\d+)/$");
            if (selectorParts.Length >= 2 && minorMatch.Groups[1].Value != selectorParts[1])
            {
                continue;
            }
            string listing = Download.ReadText("https://download.blender.org/release/" + series, false);
            foreach (Match match in Regex.Matches(listing, platform.BlenderListingPattern(major, minorMatch.Groups[1].Value)))
            {
                Version version = new Version(match.Groups[1].Value);
                if (Matches(version, selector))
                {
                    candidates.Add(new BlenderRelease(version, series));
                }
            }
        }
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("no stable Blender release matches BLENDER_VERSION=" + selector);
        }
        return candidates.OrderBy(candidate => candidate.Version).Last();
    }

    private static bool Matches(Version candidate, string selector)
    {
        string[] parts = selector.Split('.');
        if (candidate.Major != int.Parse(parts[0], CultureInfo.InvariantCulture))
        {
            return false;
        }
        if (parts.Length >= 2 && candidate.Minor != int.Parse(parts[1], CultureInfo.InvariantCulture))
        {
            return false;
        }
        return parts.Length < 3 || candidate.Build == int.Parse(parts[2], CultureInfo.InvariantCulture);
    }
}

internal static class Download
{
    public static string ReadText(string uri, bool github)
    {
        Exception last = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                HttpWebRequest request = CreateRequest(uri, github);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception exception)
            {
                last = exception;
                if (attempt < 5)
                {
                    Thread.Sleep(attempt * 2000);
                }
            }
        }
        throw new IOException("request failed after 5 attempts: " + uri, last);
    }

    public static void Save(string uri, string destination)
    {
        Exception last = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                long offset = File.Exists(destination) ? new FileInfo(destination).Length : 0;
                HttpWebRequest request = CreateRequest(uri, false);
                if (offset > 0)
                {
                    request.AddRange(offset);
                }
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    bool append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(destination, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        input.CopyTo(output);
                    }
                }
                return;
            }
            catch (Exception exception)
            {
                last = exception;
                if (attempt < 5)
                {
                    Console.Out.WriteLine("docker-godot: download interrupted; resuming attempt " + (attempt + 1) + " of 5");
                    Thread.Sleep(attempt * 2000);
                }
            }
        }
        throw new IOException("download failed after 5 attempts: " + uri, last);
    }

    private static HttpWebRequest CreateRequest(string uri, bool github)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
        request.AllowAutoRedirect = true;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        request.UserAgent = "docker-godot";
        request.Timeout = 120000;
        request.ReadWriteTimeout = 120000;
        if (github)
        {
            request.Accept = "application/vnd.github+json";
        }
        return request;
    }
}

internal static class Checksum
{
    public static void Verify(string archive, string sums, string algorithm)
    {
        string filename = Path.GetFileName(archive);
        string pattern = "^([0-9a-fA-F]+)\\s+\\*?" + Regex.Escape(filename) + "\\r?$";
        Match match = Regex.Match(File.ReadAllText(sums), pattern, RegexOptions.Multiline);
        if (!match.Success)
        {
            throw new InvalidDataException("missing " + algorithm + " checksum for " + filename);
        }

        HashAlgorithm hasher = algorithm == "SHA512" ? (HashAlgorithm)SHA512.Create() : SHA256.Create();
        string actual;
        using (hasher)
        using (FileStream input = File.OpenRead(archive))
        {
            actual = BitConverter.ToString(hasher.ComputeHash(input)).Replace("-", string.Empty);
        }
        if (!actual.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(algorithm + " checksum mismatch for " + filename);
        }
    }
}

internal static class ProcessRunner
{
    public static int Run(string executable, IEnumerable<string> arguments, bool requireSuccess)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        using (Process process = Process.Start(start))
        {
            process.WaitForExit();
            if (requireSuccess && process.ExitCode != 0)
            {
                throw new InvalidOperationException(Path.GetFileName(executable) + " exited with code " + process.ExitCode);
            }
            return process.ExitCode;
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }
        StringBuilder result = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
            }
            else if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
            }
            else
            {
                result.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
        }
        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }
}

internal sealed class PlatformInfo
{
    public static readonly PlatformInfo Current = new PlatformInfo(Environment.OSVersion.Platform == PlatformID.Win32NT);

    private PlatformInfo(bool isWindows)
    {
        IsWindows = isWindows;
        GodotRoot = isWindows ? @"C:\godot\binaries" : "/godot/binaries";
        TemplateRoot = isWindows ? @"C:\godot\export_templates" : "/godot/export_templates";
        BlenderRoot = isWindows ? @"C:\blender" : "/blender";
        StateRoot = isWindows ? @"C:\run\docker-godot" : "/run/docker-godot";
        BlenderExecutable = isWindows ? "blender.exe" : "blender";

        string home = Environment.GetEnvironmentVariable(isWindows ? "USERPROFILE" : "HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = isWindows ? @"C:\Users\ContainerAdministrator" : "/root";
        }
        string config = Environment.GetEnvironmentVariable(isWindows ? "APPDATA" : "XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config))
        {
            config = isWindows ? Path.Combine(home, "AppData", "Roaming") : Path.Combine(home, ".config");
        }
        GodotSettingsRoot = Path.Combine(config, isWindows ? "Godot" : "godot");
    }

    public bool IsWindows { get; private set; }
    public string GodotRoot { get; private set; }
    public string TemplateRoot { get; private set; }
    public string BlenderRoot { get; private set; }
    public string StateRoot { get; private set; }
    public string BlenderExecutable { get; private set; }
    public string GodotSettingsRoot { get; private set; }

    public string GodotArchive(string tag)
    {
        return IsWindows ? "Godot_v" + tag + "_win64.exe.zip" : "Godot_v" + tag + "_linux.x86_64.zip";
    }

    public string GodotExecutable(string tag)
    {
        return IsWindows ? "Godot_v" + tag + "_win64_console.exe" : "Godot_v" + tag + "_linux.x86_64";
    }

    public string BlenderArchive(string version)
    {
        return IsWindows ? "blender-" + version + "-windows-x64.zip" : "blender-" + version + "-linux-x64.tar.xz";
    }

    public string BlenderListingPattern(string major, string minor)
    {
        string suffix = IsWindows ? "windows-x64\\.zip" : "linux-x64\\.tar\\.xz";
        return "blender-(" + Regex.Escape(major) + "\\." + Regex.Escape(minor) + "\\.[0-9]+)-" + suffix;
    }

    public void EnsureTemplateLink()
    {
        if (!IsWindows)
        {
            return;
        }

        string link = Path.Combine(GodotSettingsRoot, "export_templates");
        Directory.CreateDirectory(Path.GetDirectoryName(link));
        if (Directory.Exists(link))
        {
            FileAttributes attributes = File.GetAttributes(link);
            if ((attributes & FileAttributes.ReparsePoint) == 0 && Directory.EnumerateFileSystemEntries(link).Any())
            {
                throw new IOException("Godot export template path exists and is not an image-managed junction: " + link);
            }
            Directory.Delete(link, false);
        }
        ProcessRunner.Run("cmd.exe", new[] { "/d", "/c", "mklink", "/J", link, TemplateRoot }, true);
    }
}

internal sealed class GodotRelease
{
    public GodotRelease(Version version, string tag)
    {
        Version = version;
        Tag = tag;
    }

    public Version Version { get; private set; }
    public string Tag { get; private set; }
}

internal sealed class BlenderRelease
{
    public BlenderRelease(Version version, string series)
    {
        Version = version;
        Series = series;
    }

    public Version Version { get; private set; }
    public string Series { get; private set; }
}
