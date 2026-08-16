using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

interface IReleaseResolver {
    GodotRelease ResolveGodot(VersionSelector selector);

    BlenderRelease ResolveBlender(VersionSelector selector, PlatformInfo platform);
}

sealed class ReleaseResolver : IReleaseResolver {
    const int MAX_GITHUB_PAGES = 10;
    const int GITHUB_PAGE_SIZE = 100;

    static readonly Regex godotStableTag = new("^(\\d+)\\.(\\d+)(?:\\.(\\d+))?-stable$");
    static readonly Regex blenderSeries = new("Blender(\\d+)\\.(\\d+)/");

    readonly IDownloadClient downloadClient;

    public ReleaseResolver(IDownloadClient downloadClient) {
        this.downloadClient = downloadClient;
    }

    public GodotRelease ResolveGodot(VersionSelector selector) {
        var candidates = new List<GodotRelease>();
        for (int page = 1; page <= MAX_GITHUB_PAGES; page++) {
            string uri = "https://api.github.com/repos/godotengine/godot-builds/releases?per_page=" + GITHUB_PAGE_SIZE + "&page=" + page;
            string response = downloadClient.ReadText(uri, true);
            var tags = ParseGodotTags(response);
            candidates.AddRange(ParseGodotReleases(tags, selector));
            if (candidates.Count > 0 || tags.Count < GITHUB_PAGE_SIZE) {
                break;
            }
        }

        if (candidates.Count == 0) {
            throw new InvalidOperationException("no stable Godot release matches " + EnvironmentVariableNames.GODOT_VERSION + "=" + selector);
        }

        return candidates.MaxBy(candidate => candidate.version)!;
    }

    public BlenderRelease ResolveBlender(VersionSelector selector, PlatformInfo platform) {
        string root = downloadClient.ReadText("https://download.blender.org/release/", false);
        var candidates = new List<BlenderRelease>();
        foreach (string series in ParseBlenderSeries(root, selector)) {
            string listing = downloadClient.ReadText("https://download.blender.org/release/" + series, false);
            candidates.AddRange(ParseBlenderReleases(listing, series, selector, platform));
        }

        if (candidates.Count == 0) {
            throw new InvalidOperationException("no stable Blender release matches " + EnvironmentVariableNames.BLENDER_VERSION + "=" + selector);
        }

        return candidates.MaxBy(candidate => candidate.version)!;
    }

    internal static IReadOnlyList<string> ParseGodotTags(string response) {
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException("GitHub release response is not an array");
        }

        var tags = new List<string>();
        foreach (var release in document.RootElement.EnumerateArray()) {
            if (release.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String) {
                tags.Add(tag.GetString()!);
            }
        }

        return tags;
    }

    internal static IReadOnlyList<GodotRelease> ParseGodotReleases(IEnumerable<string> tags, VersionSelector selector) {
        var releases = new List<GodotRelease>();
        foreach (string tag in tags) {
            var match = godotStableTag.Match(tag);
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
        return blenderSeries.Matches(response)
            .Cast<Match>()
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
        var match = blenderSeries.Match(series);
        if (!match.Success) {
            throw new InvalidOperationException("invalid Blender release series: " + series);
        }

        return match.Groups[2].Value;
    }
}
