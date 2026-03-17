using System.IO;

namespace _7_Zip_Password_Manager.Models;

public class ArchiveContext
{
    public string ArchiveFilePath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;

    public string ArchiveFileName => string.IsNullOrEmpty(ArchiveFilePath)
        ? string.Empty
        : Path.GetFileName(ArchiveFilePath);

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ArchiveFilePath) && File.Exists(ArchiveFilePath);
}
