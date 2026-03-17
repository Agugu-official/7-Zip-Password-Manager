using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// PasswordRankingService 评分排序测试。
/// 验证各评分因子（场景匹配、最近成功、成功率、手动优先级）的正确性和排序结果。
/// CalcScore 已设为 internal，通过 InternalsVisibleTo 可直接测试。
/// </summary>
public class PasswordRankingServiceTests
{
    private static PasswordRankingService CreateService(RankingConfig? config = null)
        => new(config);

    // ── 场景匹配：曾在同名压缩包上成功过 ──

    [Fact]
    public void SceneMatch_AddsFullScore_WhenArchiveNameMatches()
    {
        var cfg = new RankingConfig { SceneMatchScore = 40 };
        var service = CreateService(cfg);

        var entry = new PasswordEntry
        {
            Password = "abc",
            SuccessArchives = ["test.zip", "data.7z"]
        };

        var score = service.CalcScore(entry, "test.zip");

        // 场景匹配分 40 + 未使用默认分 10 = 50
        Assert.True(score >= 40, $"场景匹配后得分应 >= 40，实际 {score}");
    }

    [Fact]
    public void SceneMatch_GivesZero_WhenArchiveNameDoesNotMatch()
    {
        var service = CreateService();

        var entry = new PasswordEntry
        {
            Password = "abc",
            SuccessArchives = ["other.zip"]
        };

        var score = service.CalcScore(entry, "test.zip");

        // 无场景匹配，只有未使用默认分 10
        Assert.True(score < 40, $"无场景匹配时得分应 < 40，实际 {score}");
    }

    // ── 成功率 ──

    [Fact]
    public void SuccessRate_FullScore_WhenAlwaysSuccessful()
    {
        var cfg = new RankingConfig { SuccessRateMaxScore = 20 };
        var service = CreateService(cfg);

        var entry = new PasswordEntry
        {
            Password = "abc",
            UseCount = 10,
            SuccessCount = 10,
        };

        var score = service.CalcScore(entry, null);

        // 成功率 = 10/10 = 1.0 → 20 分
        Assert.True(score >= 20, $"100% 成功率得分应 >= 20，实际 {score}");
    }

    [Fact]
    public void SuccessRate_HalfScore_WhenHalfSuccessful()
    {
        var cfg = new RankingConfig { SuccessRateMaxScore = 20, UnusedDefaultScore = 0, ManualPriorityMaxScore = 0 };
        var service = CreateService(cfg);

        var entry = new PasswordEntry
        {
            Password = "abc",
            UseCount = 10,
            SuccessCount = 5,
        };

        var score = service.CalcScore(entry, null);

        Assert.Equal(10.0, score, precision: 1);
    }

    [Fact]
    public void UnusedEntry_GetsDefaultScore()
    {
        var cfg = new RankingConfig { UnusedDefaultScore = 10, ManualPriorityMaxScore = 0 };
        var service = CreateService(cfg);

        // UseCount = 0 的条目获得默认分
        var entry = new PasswordEntry { Password = "new-password" };
        var score = service.CalcScore(entry, null);

        Assert.Equal(10.0, score, precision: 1);
    }

    // ── 手动优先级 ──

    [Fact]
    public void ManualPriority_ClampedToMax()
    {
        var cfg = new RankingConfig
        {
            ManualPriorityMaxScore = 15,
            UnusedDefaultScore = 0,
            SuccessRateMaxScore = 0,
            RecentSuccessMaxScore = 0,
            SceneMatchScore = 0,
        };
        var service = CreateService(cfg);

        // Priority = 99 超出上限，应被 clamp 到 15
        var entry = new PasswordEntry { Password = "abc", Priority = 99 };
        var score = service.CalcScore(entry, null);

        Assert.Equal(15.0, score, precision: 1);
    }

    // ── 最近成功分（时间衰减） ──

    [Fact]
    public void RecentSuccess_FullScore_WhenJustSucceeded()
    {
        var cfg = new RankingConfig
        {
            RecentSuccessMaxScore = 25,
            DecayDays = 365,
            UnusedDefaultScore = 0,
            ManualPriorityMaxScore = 0,
            SceneMatchScore = 0,
            SuccessRateMaxScore = 0,
        };
        var service = CreateService(cfg);

        var entry = new PasswordEntry
        {
            Password = "abc",
            WasLastSuccessful = true,
            LastUsedTime = DateTime.Now, // 刚刚成功
        };

        var score = service.CalcScore(entry, null);

        // 刚刚成功 → daysSince ≈ 0 → 满分 25
        Assert.True(score >= 24.0, $"刚成功应接近满分 25，实际 {score}");
    }

    [Fact]
    public void RecentSuccess_ZeroScore_WhenNeverSuccessful()
    {
        var cfg = new RankingConfig
        {
            RecentSuccessMaxScore = 25,
            UnusedDefaultScore = 0,
            ManualPriorityMaxScore = 0,
            SceneMatchScore = 0,
            SuccessRateMaxScore = 0,
        };
        var service = CreateService(cfg);

        var entry = new PasswordEntry
        {
            Password = "abc",
            WasLastSuccessful = false,
        };

        var score = service.CalcScore(entry, null);

        Assert.Equal(0.0, score, precision: 1);
    }

    // ── 综合排序 ──

    [Fact]
    public void Rank_PutsSceneMatchedEntryFirst()
    {
        var service = CreateService();
        var archiveName = "important.zip";

        var matched = new PasswordEntry
        {
            Password = "correct",
            SuccessArchives = [archiveName],
        };
        var unmatched = new PasswordEntry
        {
            Password = "wrong",
            UseCount = 100,
            SuccessCount = 100,
        };

        var ranked = service.Rank([unmatched, matched], archiveName);

        // 场景匹配分(40) 应使 matched 排在最前面
        Assert.Equal("correct", ranked[0].Password);
        Assert.Equal("wrong", ranked[1].Password);
    }

    [Fact]
    public void Rank_PreservesAllEntries()
    {
        var service = CreateService();

        var entries = Enumerable.Range(1, 10)
            .Select(i => new PasswordEntry { Password = $"pw{i}" })
            .ToList();

        var ranked = service.Rank(entries);

        Assert.Equal(entries.Count, ranked.Count);
        foreach (var entry in entries)
            Assert.Contains(entry, ranked);
    }

    [Fact]
    public void Rank_HighPriority_BeatsLowPriority_AllElseEqual()
    {
        var cfg = new RankingConfig();
        var service = CreateService(cfg);

        var high = new PasswordEntry { Password = "high", Priority = 15 };
        var low = new PasswordEntry { Password = "low", Priority = 0 };

        var ranked = service.Rank([low, high]);

        Assert.Equal("high", ranked[0].Password);
    }
}
