using System.IO;
using System.Windows;
using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Helpers;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Views;

/// <summary>
/// 设置窗口 (二级窗口)
///
/// 职责：
///   - 让用户选择或新建密码列表 JSON 文件的存储路径
///   - 显示应用的版本和说明信息
///
/// 交互流程：
///   1. 主窗口通过 SettingsWindow.ShowWithPath(currentPath, owner) 打开本窗口
///   2. 用户通过 "浏览" 选择已有文件，或通过 "新建" 指定新文件位置
///   3. 点击 "保存并加载" → 将新路径写回 NewPath 属性，设置 DialogResult = true
///   4. 主窗口读取 NewPath 并调用 ViewModel 重新加载密码列表
///
/// 注意：
///   - 本窗口不直接操作 ViewModel，只负责路径选择
///   - 路径的持久化和密码加载由主窗口/ViewModel 完成
/// </summary>
public partial class SettingsWindow : Window
{
    private string _originalPath = string.Empty;
    private string _originalSevenZipPath = string.Empty;
    private bool _originalAutoClose;
    private int _originalMaxParallelism;
    private string _originalLanguage = AppConstants.DefaultLanguage;

    public string NewPath { get; private set; } = string.Empty;
    public string NewSevenZipPath { get; private set; } = string.Empty;
    public bool SevenZipPathChanged { get; private set; }
    public bool AutoCloseChanged { get; private set; }
    public bool NewAutoCloseValue { get; private set; }
    public bool MaxParallelismChanged { get; private set; }
    public int NewMaxParallelism { get; private set; }
    public bool LanguageChanged { get; private set; }
    public string NewLanguage { get; private set; } = AppConstants.DefaultLanguage;

    public SettingsWindow()
    {
        InitializeComponent();
        AboutVersionText.Text = GuiText.Format("settingsWindow.aboutVersion", AppVersion.GetDisplayVersion());
    }

    public record SettingsResult(bool PasswordPathChanged, string NewPasswordPath,
                                  bool SevenZipPathChanged, string NewSevenZipPath,
                                  bool AutoCloseChanged, bool NewAutoCloseValue,
                                  bool MaxParallelismChanged, int NewMaxParallelism,
                                  bool LanguageChanged, string NewLanguage);

    public static SettingsResult ShowSettings(string currentPath, string currentSevenZipPath,
                                              bool closeAfterExtract, int maxParallelism,
                                              string currentLanguage, Window owner)
    {
        var cpuCores = Data.AppConfig.CpuCoreCount;

        var win = new SettingsWindow
        {
            Owner = owner,
            _originalPath = currentPath,
            _originalSevenZipPath = currentSevenZipPath,
            _originalAutoClose = closeAfterExtract,
            _originalMaxParallelism = maxParallelism,
            _originalLanguage = currentLanguage,
            NewPath = currentPath,
            NewSevenZipPath = currentSevenZipPath,
            NewAutoCloseValue = closeAfterExtract,
            NewMaxParallelism = maxParallelism,
            NewLanguage = currentLanguage,
        };
        win.PathBox.Text = currentPath;
        win.SevenZipPathBox.Text = currentSevenZipPath;
        win.UpdateSevenZipStatus(currentSevenZipPath);
        win.InitContextMenuState();
        win.AutoCloseCheck.IsChecked = closeAfterExtract;
        win.UpdateAutoCloseStatus();

        win.CpuLabelRun.Text = GuiText.Get("settingsWindow.labelCpuPrefix");
        win.CpuCoreRun.Text = cpuCores.ToString();
        win.ThreadsSuffixRun.Text = GuiText.Get("settingsWindow.labelThreadsSuffix");
        win.ParallelSlider.Maximum = cpuCores;
        win.ParallelSlider.Value = Math.Clamp(maxParallelism, 1, cpuCores);
        win.UpdateParallelValueText();

        win.InitLanguageState(currentLanguage);

        var result = win.ShowDialog() == true;
        return new SettingsResult(
            result,
            win.NewPath,
            win.SevenZipPathChanged,
            win.NewSevenZipPath,
            win.AutoCloseChanged,
            win.NewAutoCloseValue,
            win.MaxParallelismChanged,
            win.NewMaxParallelism,
            win.LanguageChanged,
            win.NewLanguage);
    }

    // ── "浏览" 按钮：选择已有的 .json 文件 ──

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = GuiText.Get("dialogs.filterJsonPassword"),
            Title = GuiText.Get("dialogs.selectPasswordFile"),
            InitialDirectory = Path.GetDirectoryName(PathBox.Text) ?? string.Empty,
        };

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            SavePathBtn.IsEnabled = !string.Equals(PathBox.Text, _originalPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── "新建" 按钮：指定新文件保存位置 ──

    private void NewPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = GuiText.Get("dialogs.filterJsonPasswordOnly"),
            Title = GuiText.Get("dialogs.newPasswordFile"),
            FileName = "pws.json",
            InitialDirectory = Path.GetDirectoryName(PathBox.Text) ?? string.Empty,
        };

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            SavePathBtn.IsEnabled = true;
        }
    }

    // ── "保存并加载" 按钮：确认路径更改 ──

    private void SavePath_Click(object sender, RoutedEventArgs e)
    {
        NewPath = PathBox.Text;
        DialogResult = true;
        Close();
    }

    // ── 7-Zip 路径 ──

    private void DetectSevenZip_Click(object sender, RoutedEventArgs e)
    {
        var detected = Data.AppConfig.Detect7ZipPath();
        if (!string.IsNullOrEmpty(detected))
        {
            SevenZipPathBox.Text = detected;
            UpdateSevenZipStatus(detected);
            MarkSevenZipChanged();
        }
        else
        {
            SevenZipStatus.Text = GuiText.Get("settingsWindow.status7ZipNotDetected");
        }
    }

    private void BrowseSevenZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = GuiText.Get("dialogs.filter7zExe"),
            Title = GuiText.Get("dialogs.select7zExe"),
            InitialDirectory = string.IsNullOrEmpty(SevenZipPathBox.Text)
                ? AppConstants.DefaultBrowseDirectory
                : Path.GetDirectoryName(SevenZipPathBox.Text) ?? AppConstants.DefaultBrowseDirectory,
        };

        if (dialog.ShowDialog(this) == true)
        {
            SevenZipPathBox.Text = dialog.FileName;
            UpdateSevenZipStatus(dialog.FileName);
            MarkSevenZipChanged();
        }
    }

    private void UpdateSevenZipStatus(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            SevenZipStatus.Text = GuiText.Get("settingsWindow.status7ZipNotConfigured");
        }
        else if (File.Exists(path))
        {
            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                SevenZipStatus.Text = GuiText.Format("settingsWindow.status7ZipFound", info.FileVersion ?? "?");
            }
            catch
            {
                SevenZipStatus.Text = GuiText.Get("settingsWindow.status7ZipFoundNoVersion");
            }
        }
        else
        {
            SevenZipStatus.Text = GuiText.Format("settingsWindow.status7ZipFileNotExist", path);
        }
    }

    private void MarkSevenZipChanged()
    {
        NewSevenZipPath = SevenZipPathBox.Text;
        SevenZipPathChanged = !string.Equals(
            SevenZipPathBox.Text, _originalSevenZipPath, StringComparison.OrdinalIgnoreCase);
    }

    // ── 右键菜单注册 ──

    internal void InitContextMenuState()
    {
        ContextMenuCheck.IsChecked = ContextMenuService.IsRegistered();
        UpdateContextMenuStatus();
    }

    private void ContextMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ContextMenuCheck.IsChecked == true)
                ContextMenuService.Register();
            else
                ContextMenuService.Unregister();

            UpdateContextMenuStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(GuiText.Format("settingsWindow.operationFailed", ex.Message),
                GuiText.Get("settingsWindow.errorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            ContextMenuCheck.IsChecked = ContextMenuService.IsRegistered();
            UpdateContextMenuStatus();
        }
    }

    private void UpdateContextMenuStatus()
    {
        if (ContextMenuCheck.IsChecked != true)
        {
            ContextMenuStatus.Text = GuiText.Get("settingsWindow.statusContextMenuNotRegistered");
            ContextMenuDifferentInstanceHint.Visibility = Visibility.Collapsed;
            return;
        }

        var path = ContextMenuService.GetRegisteredExePath();
        if (path is not null)
        {
            var registered = GuiText.Get("settingsWindow.statusContextMenuRegistered").Trim();
            var pathLine = GuiText.Format("settingsWindow.statusContextMenuRegisteredPath", path);
            ContextMenuStatus.Text = $"{registered} —— {pathLine}";
        }
        else
        {
            ContextMenuStatus.Text = GuiText.Get("settingsWindow.statusContextMenuNotRegistered");
        }

        var currentExe = Environment.ProcessPath;
        var isDifferent = false;
        if (path is not null && !string.IsNullOrEmpty(currentExe))
        {
            try
            {
                isDifferent = !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(currentExe),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                isDifferent = !string.Equals(path, currentExe, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (isDifferent)
        {
            ContextMenuDifferentInstanceHint.Text = GuiText.Get("settingsWindow.statusContextMenuDifferentInstance");
            ContextMenuDifferentInstanceHint.Visibility = Visibility.Visible;
        }
        else
        {
            ContextMenuDifferentInstanceHint.Visibility = Visibility.Collapsed;
        }
    }

    // ── 自动关闭 ──

    private void AutoClose_Click(object sender, RoutedEventArgs e)
    {
        NewAutoCloseValue = AutoCloseCheck.IsChecked == true;
        AutoCloseChanged = NewAutoCloseValue != _originalAutoClose;
        UpdateAutoCloseStatus();
    }

    private void UpdateAutoCloseStatus()
    {
        AutoCloseStatus.Text = AutoCloseCheck.IsChecked == true
            ? GuiText.Get("settingsWindow.statusAutoCloseOn")
            : GuiText.Get("settingsWindow.statusAutoCloseOff");
    }

    // ── 并行线程数 ──

    private void ParallelSlider_ValueChanged(object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateParallelValueText();
        NewMaxParallelism = (int)ParallelSlider.Value;
        MaxParallelismChanged = NewMaxParallelism != _originalMaxParallelism;
    }

    private void UpdateParallelValueText()
    {
        if (ParallelValueText is null) return;
        ParallelValueText.Text = GuiText.Format("settingsWindow.parallelValueFormat", (int)ParallelSlider.Value);
    }

    // ── 语言切换 ──

    private void InitLanguageState(string language)
    {
        UpdateLanguageButtonStyles(language);
        LanguageStatus.Text = GuiText.Get("settingsWindow.statusLanguageCurrent");
    }

    private void LangZh_Click(object sender, RoutedEventArgs e)
        => SelectLanguage(AppConstants.DefaultLanguage);

    private void LangEn_Click(object sender, RoutedEventArgs e)
        => SelectLanguage("en");

    private void SelectLanguage(string language)
    {
        NewLanguage = language;
        LanguageChanged = !string.Equals(language, _originalLanguage, StringComparison.OrdinalIgnoreCase);
        UpdateLanguageButtonStyles(language);
        LanguageStatus.Text = LanguageChanged
            ? GuiText.Get("settingsWindow.statusLanguageRestart")
            : GuiText.Get("settingsWindow.statusLanguageCurrent");
    }

    private void UpdateLanguageButtonStyles(string language)
    {
        var isZh = string.Equals(language, AppConstants.DefaultLanguage, StringComparison.OrdinalIgnoreCase);

        var activeBg = FindResource("ButtonHover") as System.Windows.Media.Brush;
        var normalBg = FindResource("ButtonBg") as System.Windows.Media.Brush;
        var activeBorder = FindResource("WindowFg") as System.Windows.Media.Brush;
        var normalBorder = FindResource("ControlBorder") as System.Windows.Media.Brush;

        LangZhBtn.Background = isZh ? activeBg : normalBg;
        LangZhBtn.BorderBrush = isZh ? activeBorder : normalBorder;
        LangEnBtn.Background = isZh ? normalBg : activeBg;
        LangEnBtn.BorderBrush = isZh ? normalBorder : activeBorder;
    }

    // ── 关闭 ──

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
