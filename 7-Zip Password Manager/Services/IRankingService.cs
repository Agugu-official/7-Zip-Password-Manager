using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 密码评分排序服务抽象。
/// 根据历史成功率、场景匹配等因素对密码排序。
/// </summary>
public interface IRankingService
{
    List<PasswordEntry> Rank(List<PasswordEntry> entries, string? archiveFileName = null);
}
