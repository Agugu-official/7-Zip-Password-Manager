using System.Diagnostics;
using System.IO;
using System.Text.Json;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 管理"上次解压结果"的持久化读写。
/// 右键菜单解压成功后写入 last_extract.json，下次启动时读取并删除。
/// 从 ViewModel 中拆出的原因：纯文件 I/O 属于数据访问层。
/// </summary>
public class LastExtractResultService : ILastExtractResultService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public LastExtractResultService(string? filePath = null)
    {
        _filePath = filePath ?? AppDataPaths.LastExtractFile;
    }

    public void Save(LastExtractResult result)
    {
        try
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"保存上次解压记录失败: {ex.Message}");
        }
    }

    public LastExtractResult? LoadAndDelete()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            var result = JsonSerializer.Deserialize<LastExtractResult>(json);
            File.Delete(_filePath);
            return result;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"读取上次解压记录失败: {ex.Message}");
            try { File.Delete(_filePath); } catch { }
            return null;
        }
    }
}
