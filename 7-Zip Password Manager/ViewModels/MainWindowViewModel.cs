using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Helpers;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.Services;
using G = _7_Zip_Password_Manager.Helpers.GuiText;

namespace _7_Zip_Password_Manager.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ISevenZipService _sevenZipService;
    private readonly IPasswordRepository _passwordRepository;
    private readonly IRankingService _rankingService;
    private readonly ILogService _logService;
    private readonly IThemeService _themeService;
    private readonly ILastExtractResultService _lastExtractService;
    private readonly AppConfig _config;

    private CancellationTokenSource? _cts;
    private bool _isExternalLaunch;

    private string _archiveFilePath = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = G.Get("viewModel.statusReady");
    private bool _isBusy;
    private bool _isDarkMode;
    private bool _isTopMost;
    private double _tryProgress;
    private double _extractProgress;
    private DateTime _lastExtractProgressUpdate = DateTime.MinValue;
    private PasswordEntry? _selectedPassword;
    private string _searchText = string.Empty;
    private bool _shouldHighlightAddButton;

    // 排序状态：原先在 MainWindow.xaml.cs code-behind 中，
    // 属于页面状态，移到 ViewModel 以符合 MVVM。
    private string? _sortProperty;
    private ListSortDirection _sortDirection;

    public MainWindowViewModel()
    {
        _config = AppConfig.Load();
        _sevenZipService = new SevenZipService(_config.GetEffective7ZipPath());
        _passwordRepository = new PasswordRepository(_config.PasswordFilePath);
        _rankingService = new PasswordRankingService(_config.Ranking);
        _logService = new LogFileService(
            Path.Combine(AppDataPaths.ConfigFolder, AppConstants.LogFileName),
            _config.LogFileMaxSizeBytes);
        _themeService = new ThemeService();
        _lastExtractService = new LastExtractResultService();

        _isDarkMode = _config.IsDarkMode;
        _isTopMost = _config.IsTopMost;

        Passwords = new ObservableCollection<PasswordEntry>();

        // 创建 ICollectionView 并设置默认排序。
        // 原先排序状态在 MainWindow.xaml.cs 的 ApplyDefaultSort() 中，
        // 通过 Dispatcher.BeginInvoke 延迟应用。移到 ViewModel 后直接设置即可。
        PasswordsView = CollectionViewSource.GetDefaultView(Passwords);
        _sortProperty = "SuccessCount";
        _sortDirection = ListSortDirection.Descending;
        PasswordsView.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));

        BrowseArchiveCommand = new RelayCommand(_ => BrowseArchive(), _ => !IsBusy);
        BrowseOutputCommand = new RelayCommand(_ => BrowseOutput(), _ => !IsBusy);
        TryPasswordsCommand = new RelayCommand(_ => TryPasswordsAsync(), _ => CanTryPasswords());
        CancelCommand = new RelayCommand(_ => CancelTrying(), _ => IsBusy);
        AddPasswordCommand = new RelayCommand(_ => AddPassword(), _ => !IsBusy);
        RemovePasswordCommand = new RelayCommand(_ => RemovePassword(), _ => !IsBusy && SelectedPassword is not null);
        LoadPasswordsCommand = new RelayCommand(_ => BrowseAndLoadPasswords(), _ => !IsBusy);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        ToggleTopMostCommand = new RelayCommand(_ => ToggleTopMost());

        // 设计期不执行主题切换，避免设计器解析 DarkTheme 资源路径时报 XDG0003
        if (_isDarkMode && !DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            _themeService.ApplyTheme(true);

        _logService.WriteSessionStart();
        RunFirstLaunchSevenZipDetection();
        AutoLoadPasswords();
        ShowLastExtractResult();
        ConsumeStartupArchivePath();
    }

    // ── 事件：View 层订阅以执行纯 UI 操作 ──

    public event Action<PasswordEntry>? RequestStartRename;
    public event Action? RequestAutoClose;

    /// <summary>
    /// 语言切换后请求 View 层提示用户重启。
    /// 从 MainWindow.xaml.cs Settings_Click 中拆出的原因：
    /// 重启决策属于业务逻辑，但重启方式（弹窗+进程启动）属于 UI。
    /// </summary>
    public event Action? RequestRestart;

    // ── 路径 ──

    public string ArchiveFilePath
    {
        get => _archiveFilePath;
        set => SetProperty(ref _archiveFilePath, value);
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    // ── 状态 ──

    public string StatusText
    {
        get => _statusText;
        set
        {
#if DEBUG
            Debug.Assert(Application.Current?.Dispatcher.CheckAccess() != false, "StatusText must be set on UI thread.");
#endif
            SetProperty(ref _statusText, value);
        }
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public double TryProgress
    {
        get => _tryProgress;
        set
        {
#if DEBUG
            Debug.Assert(Application.Current?.Dispatcher.CheckAccess() != false, "TryProgress must be set on UI thread.");
#endif
            SetProperty(ref _tryProgress, value);
        }
    }

    public double ExtractProgress
    {
        get => _extractProgress;
        set
        {
#if DEBUG
            Debug.Assert(Application.Current?.Dispatcher.CheckAccess() != false, "ExtractProgress must be set on UI thread.");
#endif
            SetProperty(ref _extractProgress, value);
        }
    }

    // ── 密码列表 ──

    public ObservableCollection<PasswordEntry> Passwords { get; }

    /// <summary>
    /// 暴露给 View 绑定的集合视图，承载排序和搜索过滤。
    /// 原先 MainWindow.xaml.cs 通过 CollectionViewSource.GetDefaultView() 在 code-behind 中操作，
    /// 移到 ViewModel 后 View 只需绑定 ItemsSource="{Binding PasswordsView}"。
    /// </summary>
    public ICollectionView PasswordsView { get; }

    public PasswordEntry? SelectedPassword
    {
        get => _selectedPassword;
        set => SetProperty(ref _selectedPassword, value);
    }

    /// <summary>
    /// 搜索文本。setter 中自动触发 ICollectionView 过滤。
    /// 原先过滤逻辑在 MainWindow.xaml.cs 的 ApplySearchFilter/FilterPassword 中，
    /// 属于业务逻辑（判断哪些密码匹配），移到 ViewModel。
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                UpdateFilter();
        }
    }

    public bool ShouldHighlightAddButton
    {
        get => _shouldHighlightAddButton;
        set => SetProperty(ref _shouldHighlightAddButton, value);
    }

    // ── 置顶 ──

    /// <summary>
    /// 置顶状态。View 通过 Topmost="{Binding IsTopMost}" 绑定。
    /// 原先 ToggleTopMost() 直接操作 Application.Current.MainWindow.Topmost，
    /// 现在改为纯属性通知，由 XAML 绑定驱动 Window.Topmost。
    /// </summary>
    public bool IsTopMost
    {
        get => _isTopMost;
        set
        {
            if (SetProperty(ref _isTopMost, value))
                OnPropertyChanged(nameof(PinAngle));
        }
    }

    public double PinAngle => _isTopMost ? 0 : -45;

    // ── 主题 ──

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
                OnPropertyChanged(nameof(ThemeIcon));
        }
    }

    public string ThemeIcon => _isDarkMode ? "\u2600" : "\u263D";

    // ── 配置代理属性 ──

    public string PasswordFilePath => _passwordRepository.FilePath;
    public bool CloseAfterExtract => _config.CloseAfterContextMenuExtract;
    public int AutoCloseDelayMs => _config.AutoCloseDelayMs;
    public string SevenZipPath => _config.SevenZipPath;
    public int MaxParallelism => _config.MaxParallelism;
    public string Language => _config.Language;

    /// <summary>当前是否已配置或自动检测到可用的 7z.exe。</summary>
    public bool Is7ZipAvailable => _sevenZipService.IsAvailable;

    /// <summary>是否尚未显示过首次启动向导（应弹出子窗口）。</summary>
    public bool ShouldShowFirstRunWizard => !_config.FirstRunWizardShown;

    public void ChangeCloseAfterExtract(bool value)
    {
        _config.CloseAfterContextMenuExtract = value;
        _config.Save();
    }

    /// <summary>
    /// 首次启动向导确认后调用：标记已显示并保存系统集成选项（不包含右键菜单注册，由 View 层调用 ContextMenuService）。
    /// </summary>
    public void MarkFirstRunWizardComplete(bool contextMenuEnabled, bool autoCloseEnabled)
    {
        _config.FirstRunWizardShown = true;
        _config.CloseAfterContextMenuExtract = autoCloseEnabled;
        _config.Save();
    }

    public void ChangeSevenZipPath(string newPath)
    {
        _config.SevenZipPath = newPath;
        _config.Save();
        AppendLog(G.Format("viewModel.log7ZipPathUpdated", newPath));
    }

    public void ChangeMaxParallelism(int value)
    {
        _config.MaxParallelism = Math.Clamp(value, 1, Environment.ProcessorCount);
        _config.Save();
        AppendLog(G.Format("viewModel.logParallelismSet", _config.MaxParallelism));
    }

    public void ChangeLanguage(string newLanguage)
    {
        _config.Language = newLanguage;
        _config.Save();
        RequestRestart?.Invoke();
    }

    public void ChangePasswordFilePath(string newPath)
    {
        var previousPath = _passwordRepository.FilePath;
        SavePasswords();

        _passwordRepository.FilePath = newPath;

        try
        {
            _passwordRepository.Load();
        }
        catch (InvalidPasswordFileException ex)
        {
            AppendLog(G.Format("viewModel.logWrongFile", ex.Message), LogLevel.Error);
            AppendLog(G.Get("viewModel.hintSelectPwsFile"), LogLevel.Warning);
            _passwordRepository.FilePath = previousPath;
            return;
        }
        catch (Exception ex)
        {
            AppendLog(G.Format("viewModel.logLoadPasswordsFailed", ex.Message), LogLevel.Error);
            _passwordRepository.FilePath = previousPath;
            return;
        }

        _config.PasswordFilePath = newPath;
        _config.Save();
        LoadFromCurrentPath();
        AppendLog(G.Format("viewModel.logPasswordPathSwitched", newPath), LogLevel.Success);
    }

    // ── 命令 ──

    public ICommand BrowseArchiveCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand TryPasswordsCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddPasswordCommand { get; }
    public ICommand RemovePasswordCommand { get; }
    public ICommand LoadPasswordsCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleTopMostCommand { get; }

    // ── 排序（从 MainWindow.xaml.cs 迁入） ──

    /// <summary>
    /// 按列排序。由 code-behind 的列头点击事件调用。
    /// 原先整个排序状态和 CollectionView 操作在 code-behind 中，
    /// 排序状态属于页面状态，排序逻辑属于业务逻辑，均应在 ViewModel。
    /// </summary>
    public void ApplySort(string propertyName)
    {
        var direction = (propertyName == _sortProperty && _sortDirection == ListSortDirection.Ascending)
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        _sortProperty = propertyName;
        _sortDirection = direction;
        ApplySortToView();
    }

    /// <summary>
    /// 重命名开始前暂停排序，避免输入过程中条目跳动。
    /// </summary>
    public void SuspendSort()
    {
        if (PasswordsView is ListCollectionView lcv)
            lcv.CustomSort = null;
        PasswordsView.SortDescriptions.Clear();
    }

    /// <summary>
    /// 重命名结束后恢复排序。
    /// </summary>
    public void ReapplySort()
    {
        if (_sortProperty is not null)
            ApplySortToView();
    }

    private void ApplySortToView()
    {
        if (_sortProperty == "Password")
        {
            if (PasswordsView is ListCollectionView lcv)
            {
                lcv.SortDescriptions.Clear();
                lcv.CustomSort = new PasswordNaturalComparer(_sortDirection);
            }
        }
        else
        {
            if (PasswordsView is ListCollectionView lcv)
                lcv.CustomSort = null;
            PasswordsView.SortDescriptions.Clear();
            if (_sortProperty is not null)
                PasswordsView.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
        }
    }

    // ── 搜索过滤（从 MainWindow.xaml.cs 迁入） ──

    /// <summary>
    /// 根据 SearchText 设置 ICollectionView 过滤器。
    /// 原先 FilterPassword 谓词在 code-behind 中，属于业务逻辑。
    /// </summary>
    private void UpdateFilter()
    {
        if (string.IsNullOrEmpty(_searchText))
            PasswordsView.Filter = null;
        else
            PasswordsView.Filter = obj =>
                obj is PasswordEntry e
                && (e.Password?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true);
    }

    // ── 外部路径接入 ──

    public void ApplyArchivePath(string path)
    {
        _isExternalLaunch = true;
        ArchiveFilePath = path;
        var parentDir = Path.GetDirectoryName(path) ?? string.Empty;
        var archiveName = Path.GetFileNameWithoutExtension(path);
        OutputDirectory = Path.Combine(parentDir, archiveName);
        AppendLog(G.Format("viewModel.logTargetArchive", Path.GetFileName(path)));
    }

    public void AutoStartIfReady()
    {
        if (CanTryPasswords())
            TryPasswordsAsync();
    }

    private void ConsumeStartupArchivePath()
    {
        if (string.IsNullOrEmpty(App.StartupArchivePath))
            return;

        ApplyArchivePath(App.StartupArchivePath);
    }

    // ── 浏览文件 ──

    private void BrowseArchive()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = G.Get("dialogs.filterAllFiles")
                     + "|" + G.Get("dialogs.filterArchive")
        };

        if (dialog.ShowDialog() == true)
        {
            _isExternalLaunch = false;
            ArchiveFilePath = dialog.FileName;

            var parentDir = Path.GetDirectoryName(ArchiveFilePath) ?? string.Empty;
            var archiveName = Path.GetFileNameWithoutExtension(ArchiveFilePath);
            OutputDirectory = Path.Combine(parentDir, archiveName);

            AppendLog(G.Format("viewModel.logArchiveSelected", Path.GetFileName(ArchiveFilePath)));
            AppendLog(G.Format("viewModel.logOutputDirectory", OutputDirectory));
        }
    }

    private void BrowseOutput()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
            AppendLog(G.Format("viewModel.logOutputDirectory", OutputDirectory));
        }
    }

    // ── 密码管理 ──

    /// <summary>
    /// 首次启动或配置中未填写 7-Zip 路径时，自动检测一次并写入配置与日志。
    /// </summary>
    private void RunFirstLaunchSevenZipDetection()
    {
        if (!string.IsNullOrEmpty(_config.SevenZipPath) && File.Exists(_config.SevenZipPath))
            return;

        var detected = AppConfig.Detect7ZipPath();
        if (!string.IsNullOrEmpty(detected))
        {
            _config.SevenZipPath = detected;
            _config.Save();
            AppendLog(G.Format("viewModel.log7ZipAutoDetected", detected));
        }
        else
        {
            AppendLog(G.Get("viewModel.log7ZipNotDetectedHint"), LogLevel.Warning);
        }
    }

    /// <summary>
    /// 启动时自动加载。文件/目录创建已委托给 PasswordRepository.EnsureFileExists()。
    /// </summary>
    private void AutoLoadPasswords()
    {
        var (success, error) = _passwordRepository.EnsureFileExists();
        if (!success)
        {
            AppendLog(G.Format("viewModel.logPwFileCreateFailed",
                _passwordRepository.FilePath, error ?? "unknown"), LogLevel.Error);
            return;
        }

        LoadFromCurrentPath();
    }

    private void BrowseAndLoadPasswords()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = G.Get("dialogs.filterJsonPassword"),
            Title = G.Get("dialogs.selectPasswordFile"),
            InitialDirectory = Path.GetDirectoryName(_passwordRepository.FilePath) ?? string.Empty,
        };

        if (dialog.ShowDialog() != true)
            return;

        var previousPath = _passwordRepository.FilePath;
        SavePasswords();

        _passwordRepository.FilePath = dialog.FileName;

        try
        {
            _passwordRepository.Load();
        }
        catch (InvalidPasswordFileException ex)
        {
            AppendLog(G.Format("viewModel.logWrongFile", ex.Message), LogLevel.Error);
            AppendLog(G.Get("viewModel.hintSelectPwsFile"), LogLevel.Warning);
            _passwordRepository.FilePath = previousPath;
            return;
        }
        catch (Exception ex)
        {
            AppendLog(G.Format("viewModel.logLoadPasswordsFailed", ex.Message), LogLevel.Error);
            _passwordRepository.FilePath = previousPath;
            return;
        }

        _config.PasswordFilePath = dialog.FileName;
        _config.Save();
        LoadFromCurrentPath();
    }

    private void LoadFromCurrentPath()
    {
        try
        {
            var entries = _passwordRepository.Load();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int duplicateCount = 0;

            Passwords.Clear();
            foreach (var entry in entries)
            {
                if (!seen.Add(entry.Password))
                {
                    duplicateCount++;
                    continue;
                }
                Passwords.Add(entry);
            }

            AppendLog(G.Format("viewModel.logPasswordsLoaded", Passwords.Count, _passwordRepository.FilePath), LogLevel.Success);
            if (duplicateCount > 0)
            {
                AppendLog(G.Format("viewModel.logDuplicatesFiltered", duplicateCount), LogLevel.Warning);
                SavePasswords();
            }
        }
        catch (InvalidPasswordFileException ex)
        {
            AppendLog(G.Format("viewModel.logWrongFile", ex.Message), LogLevel.Error);
            AppendLog(G.Get("viewModel.hintSelectPwsFile"), LogLevel.Warning);
        }
        catch (Exception ex)
        {
            AppendLog(G.Format("viewModel.logLoadPasswordsFailed", ex.Message), LogLevel.Error);
        }
    }

    private void SavePasswords()
    {
        try
        {
            _passwordRepository.Save(Passwords.ToList());
        }
        catch (Exception ex)
        {
            AppendLog(G.Format("viewModel.logSavePasswordsFailed", ex.Message), LogLevel.Error);
        }
    }

    private void AddPassword()
    {
        ShouldHighlightAddButton = false;
        SearchText = string.Empty;
        var entry = new PasswordEntry
        {
            LastUsedTime = DateTime.Now,
            CreatedTime = DateTime.Now,
        };
        Passwords.Insert(0, entry);
        SelectedPassword = entry;
        AppendLog(G.Get("viewModel.logPasswordAdded"));
        RequestStartRename?.Invoke(entry);
    }

    private void RemovePassword()
    {
        if (SelectedPassword is null) return;
        var pwd = SelectedPassword.Password;
        Passwords.Remove(SelectedPassword);
        SelectedPassword = null;
        SavePasswords();
        AppendLog(
            G.Format("viewModel.logPasswordDeleted", pwd),
            LogLevel.Info,
            G.Format("viewModel.logPasswordDeleted", MaskPassword(pwd)));
    }

    public void AfterPasswordEdited(PasswordEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Password))
        {
            Passwords.Remove(entry);
            if (ReferenceEquals(SelectedPassword, entry))
                SelectedPassword = null;
            AppendLog(G.Get("viewModel.logEmptyPasswordDeleted"), LogLevel.Warning);
            SavePasswords();
            return;
        }

        var duplicate = Passwords.FirstOrDefault(p =>
            !ReferenceEquals(p, entry) &&
            string.Equals(p.Password, entry.Password, StringComparison.Ordinal));

        if (duplicate is not null)
        {
            Passwords.Remove(entry);
            if (ReferenceEquals(SelectedPassword, entry))
                SelectedPassword = null;
            AppendLog(
                G.Format("viewModel.logDuplicateIgnored", entry.Password),
                LogLevel.Warning,
                G.Format("viewModel.logDuplicateIgnored", MaskPassword(entry.Password)));
            SavePasswords();
            return;
        }

        SavePasswords();
    }

    // ── 核心：自动尝试密码 ──

    private bool CanTryPasswords()
    {
        return !IsBusy
               && !string.IsNullOrWhiteSpace(ArchiveFilePath)
               && Passwords.Count > 0;
    }

    private async void TryPasswordsAsync()
    {
        if (!_sevenZipService.IsAvailable)
        {
            AppendLog(G.Get("viewModel.log7ZipNotFound"), LogLevel.Error);
            StatusText = G.Get("viewModel.status7ZipNotFound");
            return;
        }

        ShouldHighlightAddButton = false;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var userToken = _cts.Token;

        var archiveFileName = Path.GetFileName(ArchiveFilePath);
        var ranked = _rankingService.Rank(Passwords.ToList(), archiveFileName);
        var total = ranked.Count;
        var parallelism = Math.Clamp(_config.MaxParallelism, 1, Environment.ProcessorCount);

        TryProgress = 0;
        ExtractProgress = 0;
        bool extractedOk = false;
        bool shouldAutoClose = false;
        PasswordEntry? correctEntry = null;

        try
        {
            // 先尝试无密码测试与解压：如果压缩包本身无需密码，可以直接解压避免多余尝试。
            var noPasswordOk = await _sevenZipService.TestPasswordAsync(
                ArchiveFilePath, string.Empty, userToken);

            if (noPasswordOk)
            {
                StatusText = G.Get("viewModel.statusExtractingNoPassword");
                ExtractProgress = 0;
                _lastExtractProgressUpdate = DateTime.MinValue;
                var extractProgress = CreateThrottledExtractProgress();
                var extractedWithoutPassword = await _sevenZipService.ExtractAsync(
                    ArchiveFilePath, string.Empty, OutputDirectory, userToken, extractProgress);

                if (extractedWithoutPassword)
                {
                    AppendLog(G.Format("viewModel.logExtractSuccessNoPassword", OutputDirectory), LogLevel.Success);
                    extractedOk = true;

                    if (_isExternalLaunch)
                    {
                        _lastExtractService.Save(new LastExtractResult
                        {
                            ArchiveFileName = archiveFileName,
                            ArchiveFilePath = ArchiveFilePath,
                            Password = string.Empty,
                            OutputDirectory = OutputDirectory,
                            ExtractTime = DateTime.Now,
                            WasAutoClose = _config.CloseAfterContextMenuExtract
                        });
                    }
                }
                else
                {
                    AppendLog(G.Get("viewModel.logExtractFailed"), LogLevel.Error);
                }
                ExtractProgress = 100;
            }
            else
            {
                // 无密码失败，再按原逻辑依次/并行尝试密码列表。
                AppendLog(G.Format("viewModel.logTryStart", total, parallelism));
                StatusText = G.Get("viewModel.statusTrying");
                if (parallelism <= 1)
                {
                    correctEntry = await TrySequential(ranked, total, archiveFileName, userToken);
                }
                else
                {
                    correctEntry = await TryParallel(ranked, total, parallelism, userToken);
                    if (correctEntry is not null)
                        correctEntry.RecordSuccessArchive(archiveFileName);
                }

                if (correctEntry is not null)
                {
                    AppendLog(
                        G.Format("viewModel.logPasswordCorrect", correctEntry.Password),
                        LogLevel.Success,
                        G.Format("viewModel.logPasswordCorrect", MaskPassword(correctEntry.Password)));
                    StatusText = G.Get("viewModel.statusExtracting");
                    ExtractProgress = 0;
                    _lastExtractProgressUpdate = DateTime.MinValue;
                    var extractProgress = CreateThrottledExtractProgress();
                    bool extracted = await _sevenZipService.ExtractAsync(
                        ArchiveFilePath, correctEntry.Password, OutputDirectory, userToken, extractProgress);

                    if (extracted)
                    {
                        AppendLog(G.Format("viewModel.logExtractSuccess", OutputDirectory), LogLevel.Success);
                        extractedOk = true;

                        if (_isExternalLaunch)
                        {
                            _lastExtractService.Save(new LastExtractResult
                            {
                                ArchiveFileName = archiveFileName,
                                ArchiveFilePath = ArchiveFilePath,
                                Password = correctEntry.Password,
                                OutputDirectory = OutputDirectory,
                                ExtractTime = DateTime.Now,
                                WasAutoClose = _config.CloseAfterContextMenuExtract
                            });
                        }
                    }
                    else
                    {
                        AppendLog(G.Get("viewModel.logExtractFailed"), LogLevel.Error);
                    }
                    ExtractProgress = 100;
                }
                else
                {
                    AppendLog(G.Get("viewModel.logAllIncorrect"), LogLevel.Warning);
                    AppendLog(G.Get("viewModel.logSuggestAddPassword"), LogLevel.Warning);
                }
            }

            // 保持进度条显示上次完成时的数值，便于确认；下次点击「开始尝试」时在开头会清空
            shouldAutoClose = extractedOk
                              && _isExternalLaunch
                              && _config.CloseAfterContextMenuExtract;

            if (shouldAutoClose)
            {
                StatusText = G.Get("viewModel.statusAutoClosing");
                AppendLog(G.Get("viewModel.logAutoClosing"), LogLevel.Success);
            }
            else
            {
                StatusText = correctEntry is not null
                    ? G.Get("viewModel.statusDoneSuccess")
                    : G.Get("viewModel.statusDoneNotFound");
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog(G.Get("viewModel.logCancelled"), LogLevel.Warning);
            StatusText = G.Get("viewModel.statusCancelled");
        }
        catch (Exception ex)
        {
            AppendLog(G.Format("viewModel.logError", ex.Message), LogLevel.Error);
            StatusText = G.Get("viewModel.statusError");
        }
        finally
        {
            IsBusy = false;
            _isExternalLaunch = false;
            _cts?.Dispose();
            _cts = null;

            SavePasswords();
            RefreshPasswordList(ranked);

            if (correctEntry is null && !shouldAutoClose)
                ShouldHighlightAddButton = true;

            if (shouldAutoClose)
                RequestAutoClose?.Invoke();
        }
    }

    /// <summary>
    /// 创建解压进度报告器：最多每 0.5 秒更新一次 ExtractProgress 与 StatusText。
    /// </summary>
    private IProgress<double> CreateThrottledExtractProgress()
    {
        return new Progress<double>(pct =>
        {
            var now = DateTime.UtcNow;
            if ((now - _lastExtractProgressUpdate).TotalSeconds < 0.5)
                return;
            _lastExtractProgressUpdate = now;
            ExtractProgress = pct;
            StatusText = G.Format("viewModel.statusExtractingProgress", (int)pct);
        });
    }

    private async Task<PasswordEntry?> TrySequential(
        List<PasswordEntry> ranked, int total, string archiveFileName,
        CancellationToken token)
    {
        for (int i = 0; i < total; i++)
        {
            token.ThrowIfCancellationRequested();

            var entry = ranked[i];
            TryProgress = (double)(i + 1) / total * 100;
            StatusText = G.Format("viewModel.statusTryingSequential", i + 1, total, MaskPassword(entry.Password));
            AppendLog(
                G.Format("viewModel.logTestingPassword", i + 1, total, entry.Password),
                LogLevel.Info,
                G.Format("viewModel.logTestingPassword", i + 1, total, MaskPassword(entry.Password)));

            bool correct = await _sevenZipService.TestPasswordAsync(
                ArchiveFilePath, entry.Password, token);

            entry.RecordUsage(correct);

            if (correct)
            {
                entry.RecordSuccessArchive(archiveFileName);
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// 并行尝试密码。进度与日志通过 Dispatcher 回 UI；RecordUsage 在工作线程的 lock 内执行，
    /// 列表在尝试过程中不实时刷新，调用方在 UI 线程的 finally 中通过 RefreshPasswordList(ranked) 统一刷新。
    /// </summary>
    private async Task<PasswordEntry?> TryParallel(
        List<PasswordEntry> ranked, int total, int parallelism,
        CancellationToken userToken)
    {
        using var foundCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(userToken, foundCts.Token);
        var linkedToken = linked.Token;
        var semaphore = new SemaphoreSlim(parallelism, parallelism);
        int completed = 0;
        PasswordEntry? correctEntry = null;
        var lockObj = new object();
        var dispatcher = Application.Current.Dispatcher;

        var tasks = ranked.Select(entry => Task.Run(async () =>
        {
            try
            {
                await semaphore.WaitAsync(linkedToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var correct = await _sevenZipService.TestPasswordAsync(
                    ArchiveFilePath, entry.Password, linkedToken);

                var done = Interlocked.Increment(ref completed);

                lock (lockObj)
                {
                    entry.RecordUsage(correct);
                }

                if (correct)
                {
                    if (Interlocked.CompareExchange(ref correctEntry, entry, null) == null)
                    {
                        try
                        {
                            _ = dispatcher.BeginInvoke(() =>
                            {
                                try
                                {
                                    TryProgress = 100;
                                    StatusText = G.Format("viewModel.statusFoundCorrect", done, total);
                                    AppendLog(
                                        G.Format("viewModel.logFoundCorrect", done, total, entry.Password),
                                        LogLevel.Success,
                                        G.Format("viewModel.logFoundCorrect", done, total, MaskPassword(entry.Password)));
                                }
                                catch (InvalidOperationException) { }
                                catch (OperationCanceledException) { }
                            });
                        }
                        catch (InvalidOperationException) { }
                        catch (OperationCanceledException) { }
                        foundCts.Cancel();
                    }
                }
                else
                {
                    try
                    {
                        _ = dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                TryProgress = (double)done / total * 100;
                                StatusText = G.Format("viewModel.statusTryingParallel", done, total, parallelism);
                            }
                            catch (InvalidOperationException) { }
                            catch (OperationCanceledException) { }
                        });
                    }
                    catch (InvalidOperationException) { }
                    catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                semaphore.Release();
            }
        }, CancellationToken.None)).ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }

        try
        {
            await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }

        userToken.ThrowIfCancellationRequested();

        if (correctEntry is null)
        {
            var done = Volatile.Read(ref completed);
            AppendLog(G.Format("viewModel.logAllTestedIncorrect", done, total));
        }

        return correctEntry;
    }

    /// <summary>
    /// 取消当前尝试/解压。可由 Cancel 命令或窗口 Closing 时调用。
    /// </summary>
    public void CancelTrying()
    {
        _cts?.Cancel();
        AppendLog(G.Get("viewModel.logCancelling"), LogLevel.Warning);
    }

    // ── 辅助 ──

    private void RefreshPasswordList(List<PasswordEntry> updated)
    {
        Passwords.Clear();
        foreach (var entry in updated)
            Passwords.Add(entry);
    }

    private static string MaskPassword(string password)
    {
        if (password.Length <= 2) return new string('*', password.Length);
        return password[0] + new string('*', password.Length - 2) + password[^1];
    }

    private void AppendLog(string message, LogLevel level = LogLevel.Info, string? fileSafeMessage = null)
    {
#if DEBUG
        Debug.Assert(Application.Current?.Dispatcher.CheckAccess() != false, "AppendLog must run on UI thread (LogEntries is not thread-safe).");
#endif
        var now = DateTime.Now;

        LogEntries.Add(new LogEntry
        {
            Timestamp = now,
            Message = message,
            Level = level
        });

        _logService.Append(now, level, fileSafeMessage ?? message);
    }

    // ── 上次解压记录 ──

    /// <summary>
    /// 启动时读取并回显上次解压结果。
    /// 数据读写已委托给 LastExtractResultService。
    /// </summary>
    private void ShowLastExtractResult()
    {
        var result = _lastExtractService.LoadAndDelete();
        if (result is null)
            return;

        AppendLog(G.Get("viewModel.logLastExtractTitle"));
        AppendLog(
            G.Format("viewModel.logLastExtractArchive", result.ArchiveFileName),
            LogLevel.Success);
        if (string.IsNullOrEmpty(result.Password))
            AppendLog(G.Get("viewModel.logLastExtractNoPassword"), LogLevel.Success);
        else
            AppendLog(
                G.Format("viewModel.logLastExtractPassword", result.Password),
                LogLevel.Success,
                G.Get("viewModel.logLastExtractPasswordMasked"));
        AppendLog(G.Format("viewModel.logLastExtractOutput", result.OutputDirectory));
        AppendLog(G.Format("viewModel.logLastExtractTime",
            result.ExtractTime.ToString("yyyy-MM-dd HH:mm:ss")));

        if (result.WasAutoClose)
            AppendLog(G.Get("viewModel.logLastExtractAutoCloseHint"), LogLevel.Warning);
    }

    // ── 列宽 / 退出 ──

    public ColumnWidthsConfig? GetColumnWidths() => _config.ColumnWidths;

    public void SaveColumnWidths(ColumnWidthsConfig widths)
    {
        _config.ColumnWidths = widths;
    }

    public void SaveOnExit()
    {
        SavePasswords();
        _config.Save();
    }

    // ── 置顶切换 ──

    private void ToggleTopMost()
    {
        IsTopMost = !IsTopMost;
        _config.IsTopMost = IsTopMost;
        _config.Save();
    }

    // ── 主题切换 ──

    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        _themeService.ApplyTheme(IsDarkMode);

        _config.IsDarkMode = IsDarkMode;
        _config.Save();
    }

    // ── 密码自然排序比较器（从 MainWindow.xaml.cs 迁入） ──

    private class PasswordNaturalComparer(ListSortDirection direction) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (x as PasswordEntry)?.Password;
            var b = (y as PasswordEntry)?.Password;
            var result = NaturalStringComparer.Instance.Compare(a, b);
            return direction == ListSortDirection.Descending ? -result : result;
        }
    }
}
