using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// 界面缩放（UI 大小）配置逻辑：默认值与 GetEffectiveUiScale 的范围夹紧
/// （容错旧版无此键 / 损坏配置 / 越界值）。
/// </summary>
public class UiScaleConfigTests
{
    [Fact]
    public void NewConfig_DefaultsToDefaultUiScale()
    {
        Assert.Equal(AppConstants.DefaultUiScale, new AppConfig().UiScale);
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.8, 0.8)]
    [InlineData(2.0, 2.0)]
    [InlineData(1.25, 1.25)]
    public void GetEffectiveUiScale_PassesThrough_WhenInRange(double input, double expected)
    {
        Assert.Equal(expected, new AppConfig { UiScale = input }.GetEffectiveUiScale(), 3);
    }

    [Theory]
    [InlineData(0.0)]   // 旧版/未设置 → 0
    [InlineData(0.3)]   // 低于下限
    [InlineData(-5.0)]  // 非法负值
    public void GetEffectiveUiScale_ClampsToMin_WhenBelowRange(double input)
    {
        Assert.Equal(AppConstants.MinUiScale, new AppConfig { UiScale = input }.GetEffectiveUiScale(), 3);
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(100.0)]
    public void GetEffectiveUiScale_ClampsToMax_WhenAboveRange(double input)
    {
        Assert.Equal(AppConstants.MaxUiScale, new AppConfig { UiScale = input }.GetEffectiveUiScale(), 3);
    }
}
