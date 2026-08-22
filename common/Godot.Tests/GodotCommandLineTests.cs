using System;
using System.IO;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class GodotCommandLineTests {
    [Test]
    public void AllowsCommandsThatDoNotImport() {
        using var directory = new TemporaryDirectory();
        string path = directory.path;

        Assert.That(() => GodotCommandLine.ValidateImportProject(["--editor"], path), Throws.Nothing);
    }

    [Test]
    public void AllowsImportFromWorkingDirectoryWithProject() {
        using var directory = new TemporaryDirectory();
        directory.Write("project.godot", string.Empty);
        string path = directory.path;

        Assert.That(() => GodotCommandLine.ValidateImportProject(["--import"], path), Throws.Nothing);
    }

    [Test]
    public void AllowsImportFromExplicitRelativeProjectPath() {
        using var directory = new TemporaryDirectory();
        string projectDirectory = Path.Combine(directory.path, "project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "project.godot"), string.Empty);
        string path = directory.path;

        Assert.That(
            () => GodotCommandLine.ValidateImportProject(["--path", "project", "--import"], path),
            Throws.Nothing);
    }

    [Test]
    public void RejectsImportWithoutProject() {
        using var directory = new TemporaryDirectory();
        string path = directory.path;

        Assert.That(
            () => GodotCommandLine.ValidateImportProject(["--headless", "--import"], path),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("cannot import without a project.godot file: " + Path.Combine(path, "project.godot")));
    }
}