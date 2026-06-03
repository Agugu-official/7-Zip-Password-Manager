using _7_Zip_Password_Manager.Helpers;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// NaturalStringComparer：资源管理器式自然排序。null 处理为纯托管逻辑，
/// 数字按值比较委托给 shlwapi 的 StrCmpLogicalW。
/// </summary>
public class NaturalStringComparerTests
{
    private readonly NaturalStringComparer _cmp = NaturalStringComparer.Instance;

    [Fact]
    public void BothNull_AreEqual()
    {
        Assert.Equal(0, _cmp.Compare(null, null));
    }

    [Fact]
    public void Null_SortsBeforeNonNull()
    {
        Assert.True(_cmp.Compare(null, "a") < 0);
        Assert.True(_cmp.Compare("a", null) > 0);
    }

    [Fact]
    public void EqualStrings_ReturnZero()
    {
        Assert.Equal(0, _cmp.Compare("file1", "file1"));
    }

    [Fact]
    public void Numbers_SortByValue_NotLexicographically()
    {
        // 字典序会把 "file10" 排在 "file2" 前面；自然排序应把 2 排在 10 前面
        Assert.True(_cmp.Compare("file2", "file10") < 0);
        Assert.True(_cmp.Compare("file10", "file2") > 0);
    }

    [Fact]
    public void Sorting_AList_ProducesNaturalOrder()
    {
        var list = new List<string?> { "img12", "img2", "img1", "img100" };
        list.Sort(_cmp);
        Assert.Equal(new[] { "img1", "img2", "img12", "img100" }, list);
    }
}
