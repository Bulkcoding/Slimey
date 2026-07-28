using System.IO;
using System.Windows.Media;
using ThrowMe.Effects;
using ThrowMe.Models;

namespace ThrowMe.Services;

/// <summary>
/// 효과음 재생 서비스. WPF MediaPlayer 풀을 사용해 겹치는 소리를 낸다.
/// wav 파일은 Resources/Sounds/ 에 두며(디자인/콘텐츠 트랙),
/// 파일이 없으면 조용히 건너뛰어 항상 안전하게 동작한다.
///
/// [파일 규약]  Resources/Sounds/{boing|splat|bonk|punch}.wav
/// (.csproj 에서 CopyToOutputDirectory 로 출력 폴더에 복사되어야 재생됨)
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly MediaPlayer[] _pool;
    private int _next;
    private bool _disposed;

    private readonly string _soundDir =
        Path.Combine(AppContext.BaseDirectory, "Resources", "Sounds");

    public AudioService(AppSettings settings, int poolSize = 6)
    {
        _settings = settings;
        _pool = new MediaPlayer[Math.Max(1, poolSize)];
        for (int i = 0; i < _pool.Length; i++)
            _pool[i] = new MediaPlayer();
    }

    /// <summary>충돌 단계에 맞는 효과음 재생. volumeScale 0~1 로 세기 반영.</summary>
    public void Play(ImpactTier tier, double volumeScale = 1.0)
    {
        string? file = tier switch
        {
            ImpactTier.Boing => "boing.wav",
            ImpactTier.Splat => "splat.wav",
            ImpactTier.Bonk => "bonk.wav",
            _ => null,
        };
        if (file != null) PlayFile(file, volumeScale);
    }

    /// <summary>클릭 펀치 효과음.</summary>
    public void PlayPunch(double volumeScale = 1.0) => PlayFile("punch.wav", volumeScale);

    private void PlayFile(string fileName, double volumeScale)
    {
        if (_disposed || !_settings.SoundEnabled) return;

        string path = Path.Combine(_soundDir, fileName);
        if (!File.Exists(path)) return; // 에셋 없으면 무음(안전)

        try
        {
            MediaPlayer player = _pool[_next];
            _next = (_next + 1) % _pool.Length;

            double vol = Math.Clamp(_settings.SoundVolume * Math.Clamp(volumeScale, 0, 1), 0, 1);
            player.Volume = vol;
            player.Open(new Uri(path, UriKind.Absolute));
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThrowMe] Audio play failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var p in _pool)
        {
            try { p.Close(); } catch { /* 무시 */ }
        }
    }
}
