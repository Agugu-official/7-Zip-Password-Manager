using System.Runtime.InteropServices;

namespace _7_Zip_Password_Manager.Helpers;

/// <summary>
/// Windows 资源管理器式自然排序（数字按值排序而非字典序）
/// </summary>
public class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return StrCmpLogicalW(x, y);
    }
}
