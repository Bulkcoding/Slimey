using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using Slimey.Models;
using Slimey.Network;
using Slimey.Services;
using Slimey.Views;

namespace Slimey;

public partial class App : Application
{
    private MonitorLayoutService? _monitorService;
    private SlimeWindow? _slimeWindow;
    private SettingsStore? _store;
    private TrayIconService? _tray;
    private AppSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 오프라인 미리보기: `--render-preview <출력폴더>` 로 실행하면 스킨/골대를 PNG로 저장하고 종료.
        if (e.Args.Length >= 2 && e.Args[0] == "--render-preview")
        {
            try { PreviewRenderer.Run(e.Args[1]); }
            catch (Exception ex) { Logger.Error("preview render failed", ex); }
            Shutdown();
            return;
        }

        RegisterGlobalExceptionHandlers();

        // --profile=<이름> : 설정 파일을 분리해 한 PC에서 여러 인스턴스를 서로 다른 노드로 띄운다(테스트용).
        // 반드시 설정을 읽기 전에 적용해야 한다.
        string? profile = e.Args
            .FirstOrDefault(a => a.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1].Trim();
        if (!string.IsNullOrWhiteSpace(profile))
        {
            SettingsStore.Profile = profile;
            AuthService.Profile = profile;
            Logger.Info($"Using settings profile '{profile}'.");
        }

        Logger.Info("Slimey starting.");

        // 이전 실행에서 받아 둔 업데이트가 있으면, 창을 만들기 전에 교체·재시작하고 즉시 종료.
        if (UpdateService.TryApplyStagedUpdate())
        {
            Logger.Info("Applying staged update; restarting.");
            Shutdown();
            return;
        }

        _store = new SettingsStore();
        _settings = _store.Load();

        _monitorService = new MonitorLayoutService();

        _slimeWindow = new SlimeWindow(_settings, _monitorService);
        if (_settings.SlimeVisible)
            _slimeWindow.Show();

        _tray = new TrayIconService(
            _settings,
            openSettings: () => _slimeWindow?.OpenSettingsPublic(),
            resetPosition: () => _slimeWindow?.ResetPositionPublic(),
            exit: Shutdown);

        // 설정 변경 시 디바운스 자동 저장.
        _store.AttachAutoSave(_settings);

        // 백그라운드로 최신 릴리스 확인 → 있으면 받아 두고 다음 실행 때 조용히 적용.
        _ = UpdateService.CheckAndStageAsync();

        Logger.Info("Slimey started.");
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("Dispatcher unhandled exception.", args.Exception);
            args.Handled = true; // 앱이 죽지 않도록
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Domain unhandled exception.", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _slimeWindow?.ShutdownCleanup();
            _tray?.Dispose();

            if (_store != null && _settings != null)
                _store.Save(_settings); // 종료 시 최종 상태 확정 저장
            _store?.Dispose();

            _monitorService?.Dispose();
            Logger.Info("Slimey exited.");
        }
        catch (Exception ex)
        {
            Logger.Error("Error during shutdown.", ex);
        }
        base.OnExit(e);
    }
}
