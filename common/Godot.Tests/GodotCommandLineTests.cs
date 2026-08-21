using System;
using System.IO;
using DockerGodot;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class GodotCommandLineTests {
    [Test]
    public void AllowsCommandsThatDoNotImport() {
        using var directory = new TemporaryDirectory();

        Assert.That(() => GodotCommandLine.ValidateImportProject(new[] { "--editor" }, directory.path), Throws.Nothing);
    }

    [Test]
    public void AllowsImportFromWorkingDirectoryWithProject() {
        using var directory = new TemporaryDirectory();
        directory.Write("project.godot", string.Empty);

        Assert.That(() => GodotCommandLine.ValidateImportProject(new[] { "--import" }, directory.path), Throws.Nothing);
    }

    [Test]
    public void AllowsImportFromExplicitRelativeProjectPath() {
        using var directory = new TemporaryDirectory();
        string projectDirectory = Path.Combine(directory.path, "project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "project.godot"), string.Empty);

        Assert.That(
            () => GodotCommandLine.ValidateImportProject(new[] { "--path", "project", "--import" }, directory.path),
            Throws.Nothing);
    }

    [Test]
    public void RejectsImportWithoutProject() {
        using var directory = new TemporaryDirectory();

        Assert.That(
            () => GodotCommandLine.ValidateImportProject(new[] { "--headless", "--import" }, directory.path),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("cannot import without a project.godot file: " + Path.Combine(directory.path, "project.godot")));
    }
}
