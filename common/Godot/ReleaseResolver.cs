using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Godot;

interface IReleaseResolver {
    GodotRelease ResolveGodot(VersionSelector selector);

    BlenderRelease ResolveBlender(VersionSelector selector, PlatformInfo platform);
}

sealed partial class ReleaseResolver : IReleaseResolver {
    readonly IDownloadClient _downloadClient;

    public ReleaseResolver(IDownloadClient downloadClient) => _downloadClient = downloadClient;

    public GodotRelease ResolveGodot(VersionSelector selector) {
        string response = _downloadClient.ReadText("https://godotengine.org/download/archive/", false);
        var candidates = ParseGodotReleases(ParseGodotArchiveTags(response), selector);

        if (candidates.Count == 0) {
            throw new InvalidOperationException("no stable Godot release matches " + EnvironmentVariableNames.GODOT_VERSION + "=" + selector);
        }

        return candidates.MaxBy(candidate => candidate.version)!;
    }

    public BlenderRelease ResolveBlender(VersionSelector selector, PlatformInfo platform) {
        string root = _downloadClient.ReadText("https://download.blender.org/release/", false);
        var candidates = new List<BlenderRelease>();
        foreach (string series in ParseBlenderSeries(root, selector)) {
            string listing = _downloadClient.ReadText("https://download.blender.org/release/" + series, false);
            candidates.AddRange(ParseBlenderReleases(listing, series, selector, platform));
        }

        if (candidates.Count == 0) {
            throw new InvalidOperationException("no stable Blender release matches " + EnvironmentVariableNames.BLENDER_VERSION + "=" + selector);
        }

        return candidates.MaxBy(candidate => candidate.version)!;
    }

    internal static IReadOnlyList<string> ParseGodotArchiveTags(string response) {
        return GodotArchiveStableTag().Matches(response)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<GodotRelease> ParseGodotReleases(IEnumerable<string> tags, VersionSelector selector) {
        var releases = new List<GodotRelease>();
        foreach (string tag in tags) {
            var match = GodotStableTag().Match(tag);
            if (!match.Success) {
                continue;
            }

            int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            var version = new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                patch);
            if (selector.Matches(version)) {
                releases.Add(new GodotRelease(version, tag));
            }
        }

        return releases;
    }

    internal static IReadOnlyList<string> ParseBlenderSeries(string response, VersionSelector selector) {
        return BlenderSeries().Matches(response)
            .Where(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) == selector.Component(0))
            .Where(match => selector.ComponentCount() < 2 || int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) == selector.Component(1))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<BlenderRelease> ParseBlenderReleases(string response, string series, VersionSelector selector, PlatformInfo platform) {
        var releases = new List<BlenderRelease>();
        foreach (Match match in Regex.Matches(response, platform.BlenderListingPattern(selector.Component(0).ToString(CultureInfo.InvariantCulture), SeriesMinor(series)))) {
            var version = new Version(match.Groups[1].Value);
            if (selector.Matches(version)) {
                releases.Add(new BlenderRelease(version, series));
            }
        }

        return releases;
    }

    static string SeriesMinor(string series) {
        var match = BlenderSeries().Match(series);
        if (!match.Success) {
            throw new InvalidOperationException("invalid Blender release series: " + series);
        }

        return match.Groups[2].Value;
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)(?:\.(\d+))?-stable$")]
    private static partial Regex GodotStableTag();

    [GeneratedRegex(@"(?<![0-9.])\d+\.\d+(?:\.\d+)?-stable(?![A-Za-z0-9.-])")]
    private static partial Regex GodotArchiveStableTag();

    [GeneratedRegex(@"Blender(\d+)\.(\d+)/")]
    private static partial Regex BlenderSeries();
}