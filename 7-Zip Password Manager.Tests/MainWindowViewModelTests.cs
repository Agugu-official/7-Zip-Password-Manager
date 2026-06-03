using System.Runtime.ExceptionServices;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.Services;
using _7_Zip_Password_Manager.ViewModels;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// MainWindowViewModel 单元测试——证明 DI 改造后 ViewModel 可在无真实服务、
/// 无 7z.exe、无文件 I/O 的情况下用手写 stub 注入并测试其逻辑。
///
/// ViewModel 仍依赖少量 WPF 基础设施（CollectionView 等），故在 STA 线程上构造。
/// 手写 stub 而非引入 Moq，以保持与主程序一致的“零额外依赖”取向。
/// </summary>
public class MainWindowViewModelTests
{
    // ── 手写 stub ──

    private sealed class StubSevenZip : ISevenZipService
    {
        public bool IsAvailable { get; init; }
        public Task<bool> TestPasswordAsync(string a, string p, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ExtractAsync(string a, string p, string o, CancellationToken ct = default, IProgress<double>? prog = null) => Task.FromResult(false);
    }

    private sealed class StubRepo : IPasswordRepository
    {
        public string FilePath { get; set; } = "stub-pws.json";
        public List<PasswordEntry> ToReturn { get; init; } = new();
        public int SaveCount { get; private set; }
        public (bool Success, string? Error) EnsureFileExists() => (true, null);
        public List<PasswordEntry> Load() => ToReturn;
        public void Save(List<PasswordEntry> entries) => SaveCount++;
    }

    private sealed class StubRanking : IRankingService
    {
        // 透传：保持顺序，便于断言
        public List<PasswordEntry> Rank(List<PasswordEntry> entries, string? archiveFileName = null) => entries;
    }

    private sealed class StubLog : ILogService
    {
        public int SessionStarts { get; private set; }
        public List<string> Messages { get; } = new();
        public void WriteSessionStart() => SessionStarts++;
        public void Append(DateTime timestamp, LogLevel level, string message) => Messages.Add(message);
    }

    private sealed class StubTheme : IThemeService
    {
        public bool? AppliedDark { get; private set; }
        public void ApplyTheme(bool dark) => AppliedDark = dark;
    }

    private sealed class StubLastExtract : ILastExtractResultService
    {
        public LastExtractResult? ToReturn { get; init; }
        public void Save(LastExtractResult result) { }
        public LastExtractResult? LoadAndDelete() => ToReturn;
    }

    // ── STA 运行器：ViewModel 构造涉及 WPF CollectionView，需 STA 线程 ──

    private static void RunSta(Action action)
    {
        ExceptionDispatchInfo? edi = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { edi = ExceptionDispatchInfo.Capture(ex); }
        })
        { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        edi?.Throw();
    }

    private static MainWindowViewModel CreateVm(
        bool sevenZipAvailable = true,
        StubRepo? repo = null,
        StubLog? log = null,
        StubLastExtract? lastExtract = null)
    {
        var config = new AppConfig
        {
            IsDarkMode = false, // 跳过主题应用（避免 WPF 资源依赖）
            // 指向一个真实存在的文件，使首启 7z 自动检测提前返回，不触发写盘
            SevenZipPath = typeof(MainWindowViewModelTests).Assembly.Location,
            FirstRunWizardShown = true,
        };

        return new MainWindowViewModel(
            new StubSevenZip { IsAvailable = sevenZipAvailable },
            repo ?? new StubRepo(),
            new StubRanking(),
            log ?? new StubLog(),
            new StubTheme(),
            lastExtract ?? new StubLastExtract(),
            config);
    }

    // ── 测试 ──

    [Fact]
    public void Ctor_WithInjectedStubs_ConstructsWithoutRealServices()
    {
        // 仅验证：用 stub 即可构造 VM（无 7z、无文件 I/O、无 Application）
        RunSta(() =>
        {
            var log = new StubLog();
            var vm = CreateVm(log: log);
            Assert.NotNull(vm);
            Assert.Equal(1, log.SessionStarts); // 构造时写入会话开始
        });
    }

    [Fact]
    public void Ctor_LoadsPasswordsFromInjectedRepository()
    {
        RunSta(() =>
        {
            var repo = new StubRepo
            {
                ToReturn = new List<PasswordEntry>
                {
                    new() { Password = "alpha" },
                    new() { Password = "beta" },
                }
            };
            var vm = CreateVm(repo: repo);

            Assert.Equal(2, vm.Passwords.Count);
            Assert.Contains(vm.Passwords, p => p.Password == "alpha");
            Assert.Contains(vm.Passwords, p => p.Password == "beta");
        });
    }

    [Fact]
    public void Ctor_DeduplicatesPasswordsByValue()
    {
        RunSta(() =>
        {
            var repo = new StubRepo
            {
                ToReturn = new List<PasswordEntry>
                {
                    new() { Password = "dup" },
                    new() { Password = "dup" },
                    new() { Password = "unique" },
                }
            };
            var vm = CreateVm(repo: repo);

            Assert.Equal(2, vm.Passwords.Count); // 重复的 "dup" 被过滤
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Is7ZipAvailable_ReflectsInjectedService(bool available)
    {
        RunSta(() =>
        {
            var vm = CreateVm(sevenZipAvailable: available);
            Assert.Equal(available, vm.Is7ZipAvailable);
        });
    }

    [Fact]
    public void Ctor_ShowsLastExtractResult_WhenServiceReturnsOne()
    {
        RunSta(() =>
        {
            var log = new StubLog();
            var last = new StubLastExtract
            {
                ToReturn = new LastExtractResult { ArchiveFileName = "demo.7z", Password = "pw" }
            };
            var vm = CreateVm(log: log, lastExtract: last);

            // 注入的“上次解压结果”应被回显到日志（具体文案经本地化，断言含归档名相关写入发生）
            Assert.NotEmpty(log.Messages);
        });
    }
}
