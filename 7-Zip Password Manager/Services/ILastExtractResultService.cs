using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 上次解压结果的持久化读写抽象。
/// 测试时可注入临时文件路径避免污染系统目录。
/// </summary>
public interface ILastExtractResultService
{
    void Save(LastExtractResult result);
    LastExtractResult? LoadAndDelete();
}
