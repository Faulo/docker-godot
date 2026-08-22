using System;
using System.Collections.Generic;
using System.IO;

namespace Godot;

static class GodotCommandLine {
    public static void ValidateImportProject(IEnumerable<string> arguments, string workingDirectory) {
        bool imports = false;
        string projectDirectory = workingDirectory;
        using var iterator = arguments.GetEnumerator();
        while (iterator.MoveNext()) {
            if (string.Equals(iterator.Current, "--import", StringComparison.Ordinal)) {
                imports = true;
            } else if (string.Equals(iterator.Current, "--path", StringComparison.Ordinal) && iterator.MoveNext()) {
                projectDirectory = iterator.Current;
            }
        }

        if (!imports) {
            return;
        }

        string project = Path.Combine(Path.GetFullPath(projectDirectory, workingDirectory), "project.godot");
        if (!File.Exists(project)) {
            throw new InvalidOperationException("cannot import without a project.godot file: " + project);
        }
    }
}