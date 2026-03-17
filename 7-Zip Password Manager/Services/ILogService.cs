using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 持久化日志写入服务的抽象，便于替换实现和单元测试。
/// </summary>
public interface ILogService
{
    void WriteSessionStart();
    void Append(DateTime timestamp, LogLevel level, string message);
}
