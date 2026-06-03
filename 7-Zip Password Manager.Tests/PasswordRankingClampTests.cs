using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// OBS-3 回归：最近成功得分的衰减系数须夹在 [0,1]。
/// 即使 LastUsedTime 位于未来（系统时钟回拨 / 跨时区），也不得超过满分。
/// </summary>
public class PasswordRankingClampTests
{
    private static readonly RankingConfig Cfg = new(); // 使用文档化默认权重

    [Fact]
    public void FutureLastUsed_RecentSuccess_DoesNotExceedMaxScore()
    {
        var svc = new PasswordRankingService(Cfg);
        var future = new PasswordEntry
        {
            Password = "p",
            WasLastSuccessful = true,
            LastUsedTime = DateTime.Now.AddDays(30), // 未来时间
            UseCount = 1,
            SuccessCount = 1,
        };

        // 期望：场景 0 + 最近成功(夹紧到满分) 25 + 成功率 20 + 优先 0 = 45
        Assert.Equal(45.0, svc.CalcScore(future, archiveFileName: null), 3);
    }

    [Fact]
    public void FutureLastUsed_DoesNotScoreHigherThanJustNow()
    {
        var svc = new PasswordRankingService(Cfg);
        PasswordEntry Make(DateTime t) => new()
        {
            Password = "p",
            WasLastSuccessful = true,
            LastUsedTime = t,
            UseCount = 1,
            SuccessCount = 1,
        };

        var now = svc.CalcScore(Make(DateTime.Now), null);
        var future = svc.CalcScore(Make(DateTime.Now.AddDays(30)), null);

        Assert.True(future <= now + 0.001, $"future={future} 不应高于 now={now}");
    }
}
