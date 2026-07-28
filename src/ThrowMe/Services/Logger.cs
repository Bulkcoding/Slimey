using System.IO;

namespace ThrowMe.Services;

/// <summary>
/// 파일 기반 경량 로거. %APPDATA%/ThrowMe/logs/ThrowMe.log 에 append 한다.
/// 로깅 자체 실패는 삼켜서 앱 동작에 영향을 주지 않는다.
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static string? _path;

    private static string Path
    {
        get
        {
            if (_path == null)
            {
                string dir = System.IO.Path.Combine(AppPaths.Roaming, "logs");
                Directory.CreateDirectory(dir);
                _path = System.IO.Path.Combine(dir, "ThrowMe.log");
            }
            return _path;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(Path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로깅 실패는 무시(앱 흐름 우선).
        }
    }
}
