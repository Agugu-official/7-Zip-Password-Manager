using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Helpers;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// 界面大小（UI 缩放）：默认值、配置吸附到最近档位、UiScale.Snap 吸附语义。
/// 档位见 AppConstants.UiScalePresets（0.5 / 0.75 / 1.0 / 1.25 / 1.5）。
/// </summary>
public class UiScaleConfigTests
{
    [Fact]
    public void NewConfig_DefaultsToDefaultUiScale()
    {
        Assert.Equal(AppConstants.DefaultUiScale, new AppConfig().UiScale);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.25, 1.25)]
    [InlineData(1.5, 1.5)]
    public void GetEffectiveUiScale_KeepsExactPresets(double input, double expected)
    {
        Assert.Equal(expected, new AppConfig { UiScale = input }.GetEffectiveUiScale(), 3);
    }

    [Theory]
    [InlineData(0.0, 0.5)]    // 旧版无此键 / 越界低 → 最低档
    [InlineData(0.3, 0.5)]
    [InlineData(0.6, 0.5)]    // 0.6 距 0.5 更近
    [InlineData(0.7, 0.75)]
    [InlineData(0.9, 1.0)]    // 旧滑块可能存的值
    [InlineData(1.1, 1.0)]
    [InlineData(2.0, 1.5)]    // 旧滑块上限 → 吸附到 1.5
    [InlineData(100.0, 1.5)]
    public void GetEffectiveUiScale_SnapsToNearestPreset(double input, double expected)
    {
        Assert.Equal(expected, new AppConfig { UiScale = input }.GetEffectiveUiScale(), 3);
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(0.74, 0.75)]
    [InlineData(1.0, 1.0)]
    [InlineData(3.0, 1.5)]
    public void UiScale_Snap_ReturnsNearestPreset(double input, double expected)
    {
        Assert.Equal(expected, UiScale.Snap(input), 3);
    }

    [Fact]
    public void UiScale_Presets_AreExpectedFiveLevels()
    {
        Assert.Equal(new[] { 0.5, 0.75, 1.0, 1.25, 1.5 }, AppConstants.UiScalePresets);
    }
}
