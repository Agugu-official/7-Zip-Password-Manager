using System.IO;
using System.IO.Pipes;
using System.Windows;
using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Helpers;
using _7_Zip_Password_Manager.Services;
using _7_Zip_Password_Manager.ViewModels;

namespace _7_Zip_Password_Manager;

public partial class App : Application
{

    private static Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;

    /// <summary>
    /// 首次启动时由命令行传入的压缩包路径（右键菜单场景）。
    /// ViewModel 构造时读取此值。
    /// </summary>
    public static string? StartupArchivePath { get; private set; }

    /// <summary>
    /// 已有实例运行时，第二个实例通过管道发来的压缩包路径。
    /// MainWindow 订阅此事件来接收文件并激活窗口。
    /// </summary>
    public static event Action<string>? ArchivePathReceived;

    /// <summary>
    /// 本次进程启动时，在首次 Load 前配置文件是否已存在（含迁移后）。
    /// 为 false 表示“本次是首次运行”，应显示首次启动向导（即使用户未手动删 config，或 ViewModel 构造时已写入 config）。
    /// </summary>
    public static bool ConfigFileExistedAtStartup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? archivePath = e.Args.Length > 0
            ? Path.GetFullPath(e.Args[0])
            : null;

        _mutex = new Mutex(true, AppConstants.MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            if (!string.IsNullOrEmpty(archivePath))
                SendPathToRunningInstance(archivePath);
            Shutdown();
            return;
        }

        AppConfig.EnsureLegacyConfigMigrated();
        ConfigFileExistedAtStartup = File.Exists(AppDataPaths.ConfigFile);

        var config = AppConfig.Load();
        // 界面缩放：设定初始档位并注入持久化回调（与注入 VM 的是同一 config 实例）。
        UiScale.Initialize(config.GetEffectiveUiScale(), s => { config.UiScale = s; config.Save(); });
        GuiText.Load(AppDataPaths.GetGuiTextFile(config.Language));
        ContextMenuService.RefreshIfRegistered();

        if (!string.IsNullOrEmpty(archivePath) && File.Exists(archivePath))
            StartupArchivePath = archivePath;

        StartPipeServer();

        var viewModel = CreateMainWindowViewModel(config);
        var mainWindow = new MainWindow(viewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// 组合根：在应用入口处手动装配服务依赖并构造主 ViewModel（手动 DI，无容器）。
    /// 这是整个对象图的唯一组装点；服务均以接口形式注入，便于替换与单元测试。
    /// </summary>
    private static MainWindowViewModel CreateMainWindowViewModel(AppConfig config)
    {
        var sevenZipService = new SevenZipService(config.GetEffective7ZipPath());
        var passwordRepository = new PasswordRepository(config.PasswordFilePath);
        var rankingService = new PasswordRankingService(config.Ranking);
        var logService = new LogFileService(
            Path.Combine(AppDataPaths.ConfigFolder, AppConstants.LogFileName),
            config.LogFileMaxSizeBytes);
        var themeService = new ThemeService();
        var lastExtractService = new LastExtractResultService();

        return new MainWindowViewModel(
            sevenZipService, passwordRepository, rankingService,
            logService, themeService, lastExtractService, config);
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        AppConstants.PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var path = await reader.ReadLineAsync(token);

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        Dispatcher.Invoke(() => ArchivePathReceived?.Invoke(path));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(AppConstants.IpcRetryDelayMs, token);
                }
            }
        }, token);
    }

    private static void SendPathToRunningInstance(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", AppConstants.PipeName, PipeDirection.Out);
            client.Connect(AppConstants.IpcConnectTimeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path);
        }
        catch
        {
            // 连接失败则静默退出，用户可再次尝试
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();

        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
