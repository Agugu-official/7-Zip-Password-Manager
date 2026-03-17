using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// AppConfig 序列化/反序列化测试。
/// 验证配置的 JSON 往返不丢失数据、格式校验生效、默认值正确。
/// 全部使用 LoadFromJson/ToJson，不涉及文件 I/O。
/// </summary>
public class AppConfigTests
{
    // ── 默认值 ──

    [Fact]
    public void NewConfig_HasCorrectDefaults()
    {
        var config = new AppConfig();

        Assert.Equal(AppConstants.DefaultLanguage, config.Language);
        Assert.Equal(2, config.MaxParallelism);
        Assert.True(config.CloseAfterContextMenuExtract);
        Assert.Equal(1000, config.AutoCloseDelayMs);
        Assert.Equal(512 * 1024, config.LogFileMaxSizeBytes);
        Assert.False(config.IsDarkMode);
        Assert.False(config.IsTopMost);
        Assert.Null(config.Ranking);
        Assert.Null(config.ColumnWidths);
    }

    // ── 序列化往返 ──

    [Fact]
    public void ToJson_ThenLoadFromJson_PreservesAllProperties()
    {
        var original = new AppConfig
        {
            IsDarkMode = true,
            IsTopMost = true,
            Language = "en",
            MaxParallelism = 4,
            CloseAfterContextMenuExtract = false,
            AutoCloseDelayMs = 2000,
            LogFileMaxSizeBytes = 1024 * 1024,
            SevenZipPath = @"D:\Tools\7z.exe",
            PasswordFilePath = @"C:\data\passwords.json",
            Ranking = new RankingConfig
            {
                SceneMatchScore = 50,
                RecentSuccessMaxScore = 30,
                DecayDays = 180,
            },
            ColumnWidths = new ColumnWidthsConfig
            {
                Password = 200,
                SuccessCount = 80,
            }
        };

        var json = original.ToJson();
        var restored = AppConfig.LoadFromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(original.IsDarkMode, restored.IsDarkMode);
        Assert.Equal(original.IsTopMost, restored.IsTopMost);
        Assert.Equal(original.Language, restored.Language);
        Assert.Equal(original.MaxParallelism, restored.MaxParallelism);
        Assert.Equal(original.CloseAfterContextMenuExtract, restored.CloseAfterContextMenuExtract);
        Assert.Equal(original.AutoCloseDelayMs, restored.AutoCloseDelayMs);
        Assert.Equal(original.LogFileMaxSizeBytes, restored.LogFileMaxSizeBytes);
        Assert.Equal(original.SevenZipPath, restored.SevenZipPath);
        Assert.Equal(original.PasswordFilePath, restored.PasswordFilePath);

        Assert.NotNull(restored.Ranking);
        Assert.Equal(50, restored.Ranking!.SceneMatchScore);
        Assert.Equal(30, restored.Ranking.RecentSuccessMaxScore);
        Assert.Equal(180, restored.Ranking.DecayDays);

        Assert.NotNull(restored.ColumnWidths);
        Assert.Equal(200, restored.ColumnWidths!.Password);
    }

    // ── 格式标识校验 ──

    [Fact]
    public void ToJson_SetsFormatAndVersion()
    {
        var config = new AppConfig();
        var json = config.ToJson();
        var restored = AppConfig.LoadFromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(AppConstants.ConfigFormat, restored!.Format);
        Assert.Equal(AppConstants.CurrentConfigVersion, restored.Version);
    }

    [Fact]
    public void LoadFromJson_RejectsWrongFormat()
    {
        // 伪造一个 format 标识不对的 JSON
        var json = """
        {
            "Format": "some-other-format",
            "Version": 1,
            "Language": "en"
        }
        """;

        var result = AppConfig.LoadFromJson(json);

        Assert.Null(result);
    }

    [Fact]
    public void LoadFromJson_AcceptsNullFormat_ForBackwardCompatibility()
    {
        // 旧版配置文件没有 Format 字段，反序列化后为 null，应视为合法
        var json = """
        {
            "Language": "en",
            "MaxParallelism": 8
        }
        """;

        var result = AppConfig.LoadFromJson(json);

        Assert.NotNull(result);
        Assert.Equal("en", result!.Language);
        Assert.Equal(8, result.MaxParallelism);
    }

    [Fact]
    public void LoadFromJson_ReturnsNull_ForEmptyOrWhitespace()
    {
        Assert.Null(AppConfig.LoadFromJson(""));
        Assert.Null(AppConfig.LoadFromJson("   "));
    }

    // ── RankingConfig 默认值 ──

    [Fact]
    public void RankingConfig_HasDocumentedDefaults()
    {
        var cfg = new RankingConfig();

        Assert.Equal(40, cfg.SceneMatchScore);
        Assert.Equal(25, cfg.RecentSuccessMaxScore);
        Assert.Equal(20, cfg.SuccessRateMaxScore);
        Assert.Equal(15, cfg.ManualPriorityMaxScore);
        Assert.Equal(10, cfg.UnusedDefaultScore);
        Assert.Equal(365, cfg.DecayDays);
    }

    // ── ColumnWidthsConfig.IsEmpty ──

    [Fact]
    public void ColumnWidthsConfig_IsEmpty_WhenAllZero()
    {
        Assert.True(new ColumnWidthsConfig().IsEmpty);
        Assert.False(new ColumnWidthsConfig { Password = 100 }.IsEmpty);
    }
}
