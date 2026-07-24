using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using Slimey.Models;
using Timer = System.Threading.Timer;

namespace Slimey.Services;

/// <summary>
/// AppSettings 를 JSON 파일로 저장/로드한다. (System.Text.Json — NuGet 불필요)
/// 경로: %APPDATA%/Slimey/settings.json
///
/// AttachAutoSave 로 설정 변경(PropertyChanged) 시 디바운스 저장하며,
/// 종료 시 Save 로 최종 상태를 확실히 남긴다.
/// </summary>
public sealed class SettingsStore : IDisposable
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    private Timer? _debounce;
    private AppSettings? _tracked;
    private bool _disposed;

    private const int DebounceMs = 400;

    public SettingsStore()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slimey");
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "settings.json");
    }

    /// <summary>저장된 설정을 읽는다. 없거나 손상 시 기본값 반환.</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var s = JsonSerializer.Deserialize<AppSettings>(json, _options);
                if (s != null)
                {
                    Logger.Info("Settings loaded.");
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Settings load failed; using defaults.", ex);
        }
        return new AppSettings();
    }

    /// <summary>즉시 저장(임시 파일 → 교체로 손상 위험 최소화).</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, _options));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error("Settings save failed.", ex);
        }
    }

    /// <summary>설정 변경을 구독해 디바운스 저장한다.</summary>
    public void AttachAutoSave(AppSettings settings)
    {
        _tracked = settings;
        settings.PropertyChanged += OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                var s = _tracked;
                if (s != null) Save(s);
            }, null, DebounceMs, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_tracked != null) _tracked.PropertyChanged -= OnChanged;
        lock (_lock) { _debounce?.Dispose(); }
    }
}
