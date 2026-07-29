using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using ThrowMe.Models;
using ThrowMe.Network;
using ThrowMe.Services;
using ThrowMe.Views;

namespace ThrowMe;

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

        // 데이터 폴더 확정 + 예전 이름(ThrowMe) 폴더에서 이관. 설정을 읽기 전에 끝내야 한다.
        AppPaths.Initialize();
        foreach (string line in AppPaths.TakePendingLog())
            Logger.Info(line);

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

        Logger.Info("ThrowMe starting.");

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

        // 방금 업데이트가 적용됐다면 변경 내용을 한 번 보여준다.
        ShowReleaseNotesIfJustUpdated();

        // 백그라운드로 최신 릴리스 확인 → 받아 두면 그 즉시 적용을 제안한다(아래 핸들러).
        UpdateService.UpdateStaged += OnUpdateStaged;
        _ = UpdateService.CheckAndStageAsync();

        Logger.Info("ThrowMe started.");
    }

    /// <summary>
    /// 새 버전을 받아 둔 직후. 실행 중인 exe 는 스스로를 덮어쓸 수 없어 재시작이 꼭 필요하므로,
    /// 지금 재시작할지 물어본다. (예전에는 그냥 다음 실행을 기다려서, 사용자가 앱을 두 번
    /// 켜야 새 버전이 됐다.) 거절하면 다음 실행 때 조용히 적용된다.
    /// </summary>
    private void OnUpdateStaged(Version version)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (_settings is { AutoRestartOnUpdate: false }) return; // 사용자가 꺼 둠

                var prompt = new UpdatePromptWindow(version, _settings);
                prompt.ShowDialog();
                if (!prompt.RestartNow) return;

                // 확인창에서 이미 노트를 봤으면 교체 후 같은 내용이 또 뜨지 않게 한다.
                prompt.MarkNotesSeenIfShown();

                if (UpdateService.TryApplyStagedUpdate())
                {
                    Logger.Info($"Applying staged update v{version} on user request; restarting.");
                    Shutdown(); // 교체 스크립트가 종료를 기다렸다가 새 버전으로 다시 실행한다
                }
                else
                {
                    Logger.Error("Staged update could not be applied (write permission?).");
                    MessageBox.Show(
                        "업데이트를 적용하지 못했습니다. 설치 위치에 쓰기 권한이 있는지 확인해 주세요.",
                        "ThrowMe", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to prompt/apply staged update.", ex);
            }
        });
    }

    /// <summary>
    /// 업데이트 직후라면 릴리스 노트 팝업을 띄운다.
    /// 노트는 1회성이라, 설정으로 꺼 둔 경우에도 소비(삭제)해서 다음 업데이트 때 옛 노트가 뜨지 않게 한다.
    /// </summary>
    private void ShowReleaseNotesIfJustUpdated()
    {
        try
        {
            var notes = UpdateService.TryConsumeAppliedNotes();
            if (notes == null) return;

            Logger.Info($"Updated to v{notes.Version}.");
            if (_settings is { ShowReleaseNotes: false }) return;

            // 슬라임 창이 자리를 잡은 뒤 뜨도록 살짝 미룬다(시작을 막지 않음).
            //
            // [주의] 예전에는 DispatcherPriority.ApplicationIdle 로 예약했는데, 물리 렌더 루프
            // (CompositionTarget.Rendering)가 도는 동안에는 dispatcher 가 idle 이 되지 않아
            // 콜백이 실행되지 않았다. 공이 정지해 있을 때만 팝업이 뜨고, 정작 업데이트 직후
            // 릴레이로 공을 받아 루프가 돌면 영영 안 떴다.
            // 타이머는 idle 여부와 무관하게 발화하므로 확실하다.
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { new ReleaseNotesWindow(notes, _settings).Show(); }
                catch (Exception ex) { Logger.Error("Failed to show release notes.", ex); }
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Release notes check failed.", ex);
        }
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
            Logger.Info("ThrowMe exited.");
        }
        catch (Exception ex)
        {
            Logger.Error("Error during shutdown.", ex);
        }
        base.OnExit(e);
    }
}
