using System;
using System.Globalization;

sealed class VersionSelector {
    readonly int[] components;

    VersionSelector(int[] components) {
        this.components = components;
    }

    public static VersionSelector Parse(string name, string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException(name + " is required");
        }

        string[] parts = value.Split('.');
        if (parts.Length is < 1 or > 3) {
            throw Invalid(name, value);
        }

        int[] components = new int[parts.Length];
        for (int index = 0; index < parts.Length; index++) {
            if (parts[index].Length == 0 || !int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out components[index])) {
                throw Invalid(name, value);
            }
        }

        return new VersionSelector(components);
    }

    public bool Matches(Version version) {
        return components[0] == version.Major
            && (components.Length < 2 || components[1] == version.Minor)
            && (components.Length < 3 || components[2] == version.Build);
    }

    public int Component(int index) {
        return components[index];
    }

    public int ComponentCount() {
        return components.Length;
    }

    public override string ToString() {
        return string.Join('.', components);
    }

    static InvalidOperationException Invalid(string name, string value) {
        return new InvalidOperationException(name + " must contain one to three numeric components, got: " + value);
    }
}
