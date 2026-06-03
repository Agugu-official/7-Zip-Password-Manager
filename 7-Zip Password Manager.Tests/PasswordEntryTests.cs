using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// PasswordEntry 的使用统计与场景记录逻辑——这些字段直接影响排名评分，须正确。
/// </summary>
public class PasswordEntryTests
{
    [Fact]
    public void RecordUsage_Failure_IncrementsUseCountOnly()
    {
        var e = new PasswordEntry { Password = "p" };
        e.RecordUsage(false);

        Assert.Equal(1, e.UseCount);
        Assert.Equal(0, e.SuccessCount);
        Assert.False(e.WasLastSuccessful);
    }

    [Fact]
    public void RecordUsage_Success_IncrementsBothCounts()
    {
        var e = new PasswordEntry { Password = "p" };
        e.RecordUsage(true);

        Assert.Equal(1, e.UseCount);
        Assert.Equal(1, e.SuccessCount);
        Assert.True(e.WasLastSuccessful);
    }

    [Fact]
    public void RecordUsage_FailureAfterSuccess_FlipsWasLastSuccessful()
    {
        var e = new PasswordEntry { Password = "p" };
        e.RecordUsage(true);
        e.RecordUsage(false);

        Assert.Equal(2, e.UseCount);
        Assert.Equal(1, e.SuccessCount);
        Assert.False(e.WasLastSuccessful); // 反映最近一次结果
    }

    [Fact]
    public void RecordUsage_UpdatesLastUsedTime()
    {
        var e = new PasswordEntry { Password = "p" };
        var before = DateTime.Now;
        e.RecordUsage(false);
        Assert.True(e.LastUsedTime >= before);
    }

    [Fact]
    public void RecordSuccessArchive_AddsName()
    {
        var e = new PasswordEntry { Password = "p" };
        e.RecordSuccessArchive("data.7z");
        Assert.Contains("data.7z", e.SuccessArchives);
    }

    [Fact]
    public void RecordSuccessArchive_Deduplicates_CaseInsensitively()
    {
        var e = new PasswordEntry { Password = "p" };
        e.RecordSuccessArchive("Data.7z");
        e.RecordSuccessArchive("data.7z");
        e.RecordSuccessArchive("DATA.7Z");

        Assert.Single(e.SuccessArchives);
    }
}
