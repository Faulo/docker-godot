using System;
using Xunit;

public sealed class GodotEditorSettingsTests {
    const string SETTING = "filesystem/import/blender/blender_path = \"/blender/blender\"";

    [Fact]
    public void CreatesCompleteSettingsDocument() {
        string result = GodotEditorSettings.UpdateContents(null, SETTING);

        Assert.Equal(
            "[gd_resource type=\"EditorSettings\" format=3]" + Environment.NewLine + Environment.NewLine
                + "[resource]" + Environment.NewLine + SETTING + Environment.NewLine,
            result);
    }

    [Fact]
    public void AppendsSettingWithoutDiscardingExistingSettings() {
        string existing = "[resource]" + Environment.NewLine + "interface/editor/display_scale = 1.0" + Environment.NewLine;

        string result = GodotEditorSettings.UpdateContents(existing, SETTING);

        Assert.StartsWith(existing, result, StringComparison.Ordinal);
        Assert.EndsWith(SETTING + Environment.NewLine, result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacesExistingBlenderSettingInPlace() {
        string existing = "[resource]" + Environment.NewLine
            + "filesystem/import/blender/blender_path = \"old\"" + Environment.NewLine
            + "interface/editor/display_scale = 1.0" + Environment.NewLine;

        string result = GodotEditorSettings.UpdateContents(existing, SETTING);

        Assert.DoesNotContain("\"old\"", result, StringComparison.Ordinal);
        Assert.Contains(SETTING, result, StringComparison.Ordinal);
        Assert.Contains("interface/editor/display_scale", result, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesCrLfAroundReplacedSetting() {
        string existing = "[resource]\r\nfilesystem/import/blender/blender_path = \"old\"\r\nnext = true\r\n";

        string result = GodotEditorSettings.UpdateContents(existing, SETTING);

        Assert.Equal("[resource]\r\n" + SETTING + "\r\nnext = true\r\n", result);
    }
}
