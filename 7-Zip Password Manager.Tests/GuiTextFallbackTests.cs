using System.Text.Json;
using _7_Zip_Password_Manager.Helpers;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// 校验 GuiText 内嵌的 zh-CN 兜底 JSON（gui.json 编码损坏时启用）始终合法，
/// 且与新增的界面缩放文案保持同步——守护那段超长单行字面量，防止被改坏。
/// </summary>
public class GuiTextFallbackTests
{
    [Fact]
    public void EmbeddedZhCnFallback_IsValidJson_WithExpectedTitle()
    {
        using var doc = JsonDocument.Parse(GuiText.GetZhCnFallbackJson());
        Assert.Equal("7-Zip 密码管理器",
            doc.RootElement.GetProperty("mainWindow").GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("sectionDisplay")]
    [InlineData("labelUiScale")]
    [InlineData("uiScaleValueFormat")]
    [InlineData("labelUiScaleHint")]
    public void EmbeddedZhCnFallback_ContainsUiScaleKeys(string key)
    {
        using var doc = JsonDocument.Parse(GuiText.GetZhCnFallbackJson());
        var settings = doc.RootElement.GetProperty("settingsWindow");
        Assert.True(settings.TryGetProperty(key, out _), $"内嵌兜底缺少键 settingsWindow.{key}");
    }
}
