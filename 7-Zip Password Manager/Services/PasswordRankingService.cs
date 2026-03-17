using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 多因素评分排序：场景匹配 + 最近成功 + 成功率 + 手动优先。
/// 各项权重由 <see cref="RankingConfig"/> 配置。
/// </summary>
public class PasswordRankingService : IRankingService
{
    private readonly RankingConfig _cfg;

    public PasswordRankingService(RankingConfig? config = null)
    {
        _cfg = config ?? new RankingConfig();
    }

    public List<PasswordEntry> Rank(List<PasswordEntry> entries, string? archiveFileName = null)
    {
        return entries
            .Select(e => (Entry: e, Score: CalcScore(e, archiveFileName)))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Entry)
            .ToList();
    }

    internal double CalcScore(PasswordEntry e, string? archiveFileName)
    {
        double score = 0;

        if (!string.IsNullOrEmpty(archiveFileName)
            && e.SuccessArchives.Contains(archiveFileName, StringComparer.OrdinalIgnoreCase))
        {
            score += _cfg.SceneMatchScore;
        }

        if (e.WasLastSuccessful && e.LastUsedTime > DateTime.MinValue)
        {
            var daysSince = (DateTime.Now - e.LastUsedTime).TotalDays;
            score += _cfg.RecentSuccessMaxScore * Math.Max(0, 1.0 - daysSince / _cfg.DecayDays);
        }

        if (e.UseCount > 0)
            score += _cfg.SuccessRateMaxScore * e.SuccessCount / e.UseCount;
        else
            score += _cfg.UnusedDefaultScore;

        score += Math.Clamp(e.Priority, 0, _cfg.ManualPriorityMaxScore);

        return score;
    }
}
