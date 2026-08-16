using System;
using NUnit.Framework;

public sealed class GodotEditorSettingsTests {
    const string SETTING = "filesystem/import/blender/blender_path = \"/blender/blender\"";

    [Test]
    public void CreatesCompleteSettingsDocument() {
        string result = GodotEditorSettings.UpdateContents(null, SETTING);

        Assert.That(result, Is.EqualTo(
            "[gd_resource type=\"EditorSettings\" format=3]" + Environment.NewLine + Environment.NewLine
                + "[resource]" + Environment.NewLine + SETTING + Environment.NewLine));
    }

    [Test]
    public void AppendsSettingWithoutDiscardingExistingSettings() {
        string existing = "[resource]" + Environment.NewLine + "interface/editor/display_scale = 1.0" + Environment.NewLine;

        string result = GodotEditorSettings.UpdateContents(existing, SETTING);

        Assert.That(result, Does.StartWith(existing));
        Assert.That(result, Does.EndWith(SETTING + Environment.NewLine));
    }

    [Test]
    public void ReplacesExistingBlenderSettingInPlace() {
        string existing = "[resource]" + Environment.NewLine
            + "filesystem/import/blender/blender_path = \"old\"" + Environment.NewLine
            + "interface/editor/display_scale = 1.0" + Environment.NewLine;

        string result = GodotEditorSettings.UpdateContents(existing, SETTING);

        Assert.That(result, Does.Not.Contain("\"old\""));
        Assert.That(result, Does.Contain(SETTING));
        Assert.That(result, Does.Contain("interface/editor/display_scale"));
    }

    [Test]
    public void PreservesCrLfAroundReplacedSetting() {
        string existing = "[resource]\r\nfilesystem/import/blender/blender_path = \"old\"\r\nnext = true\r\n";

        string result = GodotEditorSettings.UpdateContents(existing, SETTING);

        Assert.That(result, Is.EqualTo("[resource]\r\n" + SETTING + "\r\nnext = true\r\n"));
    }
}
