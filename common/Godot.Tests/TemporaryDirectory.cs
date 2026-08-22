using System;
using System.IO;

namespace Godot.Tests;

sealed class TemporaryDirectory : IDisposable {
    public TemporaryDirectory() {
        path = Path.Combine(Path.GetTempPath(), "docker-godot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
    }

    public string path { get; }

    public void Dispose() {
        if (Directory.Exists(path)) {
            Directory.Delete(path, true);
        }
    }

    public string Write(string relativePath, string contents) {
        string destination = Path.Combine(path, relativePath);
        File.WriteAllText(destination, contents);
        return destination;
    }
}