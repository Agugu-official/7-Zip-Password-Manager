using System.Diagnostics;
using System.IO;
using System.Text.Json;
using _7_Zip_Password_Manager.Constants;

namespace _7_Zip_Password_Manager.Helpers;

/// <summary>
/// 提供 GUI 文本的集中访问，所有用户可见字符串均从 gui.json 加载。
/// 为未来 i18n 多语言切换预留扩展点。
/// </summary>
public static class GuiText
{
    private static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal);

    /// <summary>
    /// 从 JSON 文件加载所有文本条目。应在应用启动时调用一次。
    /// </summary>
    public static void Load(string path)
    {
        Strings.Clear();

        if (!File.Exists(path))
        {
            Trace.TraceWarning($"GUI text file not found: {path}");
            return;
        }

        try
        {
            var json = File.ReadAllBytes(path);
            // 跳过 UTF-8 BOM，否则 JsonDocument.Parse 会报 "0xEF is an invalid start of a value"
            if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
                json = json.AsSpan(3).ToArray();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("format", out var formatEl) && root.TryGetProperty("version", out var versionEl))
            {
                var format = formatEl.GetString();
                var version = versionEl.ValueKind == JsonValueKind.Number && versionEl.TryGetInt32(out var v) ? v : -1;
                if (!string.Equals(format, AppConstants.GuiFormat, StringComparison.OrdinalIgnoreCase) || version != AppConstants.CurrentGuiVersion)
                    Trace.TraceWarning($"GUI 文本文件格式或版本不匹配（期望 \"{AppConstants.GuiFormat}\" 版本 {AppConstants.CurrentGuiVersion}，实际 \"{format}\" 版本 {version}），继续使用。");
            }
            Flatten(root, prefix: string.Empty);

            // 若从文件加载的标题含问号，说明 gui.json 编码损坏，用内嵌中文回退
            if (Strings.TryGetValue("mainWindow.title", out var title) && title.Contains('?'))
                ApplyZhCnFallback();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Failed to load GUI text: {ex.Message}");
        }
    }

    /// <summary>
    /// 当 gui.json 因编码损坏显示为 "????" 时，用内嵌的 zh-CN 字符串覆盖（Unicode 转义，避免文件编码问题）。
    /// </summary>
    private static void ApplyZhCnFallback()
    {
        var json = GetZhCnFallbackJson();
        try
        {
            using var doc = JsonDocument.Parse(json);
            Flatten(doc.RootElement, prefix: string.Empty);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Zh-CN fallback failed: {ex.Message}");
        }
    }

    private static string GetZhCnFallbackJson() => """
        {"mainWindow":{"title":"7-Zip 密码管理器","tooltipPin":"窗口置顶 / 取消置顶","tooltipTheme":"切换亮色 / 暗色主题","labelArchive":"压缩包：","btnBrowse":"浏览...","labelOutput":"解压到：","btnSelect":"选择...","btnSettings":"设置","btnStart":"开始尝试","btnCancel":"取消","btnLoadPasswords":"加载密码列表","btnAddPassword":"添加密码","btnDeletePassword":"删除密码","headerPasswordList":"密码列表","colPassword":"密码","colSuccessCount":"成功次数","colUseCount":"使用次数","colLastUsed":"最近使用","colSuccess":"成功","headerLog":"日志","searchPlaceholder":"搜索密码...（Ctrl+F）","statusBarLabelTry":"尝试","statusBarLabelExtract":"解压"},"settingsWindow":{"title":"设置","sectionPasswordPath":"密码文件路径","labelCurrentPwFile":"当前密码文件：","btnNew":"新建...","btnSaveAndLoad":"保存并加载","section7Zip":"7-Zip 路径","btnCheck":"自动检测","sectionIntegration":"系统集成","labelContextMenu":"为 Windows 添加右键菜单「7ZPM - 智能解压」","statusContextMenuRegisteredWithPath":"已注册 —— 当前指向：{0}","statusContextMenuNotRegistered":"未注册。勾选即可添加。","labelAutoClose":"右键菜单解压成功后自动关闭窗口","sectionPerformance":"性能","labelParallelThreads":"并行测试线程数","labelCpuPrefix":"CPU：","labelThreadsSuffix":" 线程","labelParallelHint":"同时运行的 7z.exe 进程数量。数值越高速度越快，但占用更多 CPU。","sectionAbout":"关于","aboutAppName":"7-Zip 密码管理器","aboutVersion":"版本 {0}","aboutDescription":"管理历史密码并自动尝试解压加密压缩包的小工具。基于本地 7z.exe，多维度智能排序，尽量用最少次数找到正确密码。","aboutLicense":"本软件以 AGPL-3.0 许可证开源发布。","status7ZipNotDetected":"未检测到 7-Zip，请手动选择 7z.exe。","status7ZipNotConfigured":"尚未配置。点击\"自动检测\"以尝试自动查找。","status7ZipFound":"✔ 已找到 7-Zip {0}","status7ZipFoundNoVersion":"✔ 已找到 7z.exe","status7ZipFileNotExist":"✘ 文件不存在：{0}","statusContextMenuRegistered":"✔ 已注册","statusContextMenuRegisteredPath":"当前指向：{0}","statusContextMenuDifferentInstance":"与当前运行的不是同一份。","statusAutoCloseOn":"已开启——解压成功后 1 秒自动关闭窗口","statusAutoCloseOff":"已关闭——解压完成后保持窗口打开","parallelValueFormat":"{0} 线程","sectionLanguage":"界面语言","statusLanguageCurrent":"当前语言：中文","statusLanguageRestart":"语言已更改，重启后生效","restartTitle":"需要重启","restartMessage":"语言已更改，需要重启应用后才会生效。\n现在重启吗？","operationFailed":"操作失败：{0}","errorTitle":"错误"},"firstRun":{"title":"首次设置","message7ZipNotFound":"未找到 7-Zip。如需使用，请自行下载安装。","link7ZipText":"打开 7-Zip 官网","hintSettingsLater":"稍后，你可以在设置中找到此选项。","btnOk":"确定"},"viewModel":{"statusReady":"就绪","log7ZipPathUpdated":"已更新 7-Zip 路径：{0}","log7ZipAutoDetected":"已自动检测到 7-Zip：{0}","log7ZipNotDetectedHint":"未检测到 7-Zip，请到设置中配置 7z.exe 路径。","logParallelismSet":"并行线程数已设置为 {0}","logWrongFile":"选择的文件不正确：{0}","hintSelectPwsFile":"提示：请选择本程序生成的密码文件（pws.json），不要选择其他 JSON 文件。","logLoadPasswordsFailed":"加载密码列表失败：{0}","logPasswordPathSwitched":"密码文件路径已切换：{0}","logTargetArchive":"目标压缩包：{0}","logArchiveSelected":"已选择压缩包：{0}","logOutputDirectory":"输出目录：{0}","logDirectoryCreated":"已创建目录：{0}","logDirectoryCreateFailed":"创建目录失败：{0} — {1}","logDefaultPwFileCreated":"已创建默认密码文件：{0}","logPwFileCreateFailed":"创建密码文件失败：{0} — {1}","logPasswordsLoaded":"已加载 {0} 条密码 ← {1}","logDuplicatesFiltered":"已自动过滤 {0} 条重复密码","logSavePasswordsFailed":"保存密码列表失败：{0}","logPasswordAdded":"已添加一条密码记录","logPasswordDeleted":"已删除密码：{0}","logEmptyPasswordDeleted":"已自动删除空密码","logDuplicateIgnored":"密码\"{0}\"已存在，已忽略重复项","log7ZipNotFound":"未找到 7z.exe，请确认已安装 7-Zip。","status7ZipNotFound":"未找到 7z.exe","logTryStart":"开始尝试 {0} 个密码（{1} 路并行）...","statusTrying":"正在尝试密码...","logPasswordCorrect":"找到正确密码：{0}","statusExtracting":"已找到正确密码，正在解压...","statusExtractingNoPassword":"检测到无需密码，正在解压...","statusExtractingProgress":"解压中 {0}%","logExtractSuccess":"解压成功 → {0}","logExtractSuccessNoPassword":"未检测到加密，直接解压成功 → {0}","logExtractFailed":"解压失败。请检查输出目录或磁盘空间。","logAllIncorrect":"所有候选密码均不正确。","logSuggestAddPassword":"提示：点击\"添加密码\"保存正确密码，方便下次自动使用。","statusAutoClosing":"解压成功——窗口将在 1 秒后自动关闭","logAutoClosing":"解压完成，窗口稍后自动关闭。","statusDoneSuccess":"就绪 - 解压成功","statusDoneNotFound":"就绪 - 未找到正确密码","logCancelled":"操作已取消。","statusCancelled":"已取消","logError":"错误：{0}","statusError":"错误","statusTryingSequential":"正在尝试（{0}/{1}）：{2}","logTestingPassword":"[{0}/{1}] 正在测试：{2}","statusFoundCorrect":"已完成 {0}/{1} — 已找到正确密码","logFoundCorrect":"[{0}/{1}] {2} — 正确","statusTryingParallel":"正在尝试（{0}/{1}，{2} 路并行）","logAllTestedIncorrect":"已测试 {0}/{1} 个密码，均不正确。","logCancelling":"正在取消...","logLastExtractTitle":"━━ 上次智能解压结果 ━━","logLastExtractArchive":"压缩包：{0}","logLastExtractNoPassword":"上次解压为无密码解压，未使用密码。","logLastExtractPassword":"使用的密码：{0}","logLastExtractPasswordMasked":"使用的密码：***（在文件日志中已隐藏，仅在应用内可见）","logLastExtractOutput":"输出目录：{0}","logLastExtractTime":"时间：{0}","logLastExtractAutoCloseHint":"（此记录来自上一次\"自动关闭\"解压过程。如需查看完整过程，请先关闭自动关闭功能。）"},"dialogs":{"selectPasswordFile":"选择密码文件","filterJsonPassword":"JSON 密码文件 (*.json)|*.json|所有文件 (*.*)|*.*","newPasswordFile":"新建密码文件","filterJsonPasswordOnly":"JSON 密码文件 (*.json)|*.json","select7zExe":"选择 7z.exe","filter7zExe":"7z.exe|7z.exe|所有文件 (*.*)|*.*","filterAllFiles":"所有文件 (*.*)|*.*","filterArchive":"常见压缩包（含分卷）|*.7z;*.zip;*.rar;*.tar;*.gz;*.bz2;*.xz;*.zst;*.lz;*.lzma;*.cab;*.iso;*.wim;*.arj;*.lzh;*.z;*.cpio;*.rpm;*.deb;*.tgz;*.tbz2;*.txz;*.001;*.r00;*.r01;*.z01"},"services":{"contextMenuText":"7ZPM - 智能解压","cannotGetExePath":"无法确定当前可执行文件路径。","invalidFileUnrecognized":"文件\"{0}\"不是有效的密码文件（内容格式无法识别）。","invalidFileArrayNoEntries":"文件\"{0}\"是 JSON 数组，但不包含密码条目结构。请使用本程序生成的密码文件。","invalidFileArrayParseFailed":"文件\"{0}\"不是有效的密码文件，JSON 数组无法解析为密码条目。","invalidFileJsonParseFailed":"文件\"{0}\"的 JSON 结构无法解析。请确认选择了正确的文件。","invalidFileWrongFormat":"文件\"{0}\"不是 7-Zip 密码管理器的密码文件。（期望格式标识 \"{1}\"，实际为 \"{2}\"）","emptyValue":"（空）"}}
        """;

    /// <summary>
    /// 按点分键获取文本。找不到时返回键本身，便于排查遗漏。
    /// </summary>
    public static string Get(string key)
    {
        return Strings.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// 获取文本并用 <see cref="string.Format(string,object[])"/> 填充占位符。
    /// </summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// 递归展平 JSON 为 "section.key" → "value" 的扁平字典。
    /// 仅收集字符串叶节点，忽略 format / version / language 等元数据。
    /// </summary>
    private static void Flatten(JsonElement element, string prefix)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix)
                    ? property.Name
                    : $"{prefix}.{property.Name}";

                if (property.Value.ValueKind == JsonValueKind.Object)
                    Flatten(property.Value, key);
                else if (property.Value.ValueKind == JsonValueKind.String)
                    Strings[key] = property.Value.GetString() ?? string.Empty;
            }
        }
    }
}
