using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Slimey.Models;
using Slimey.Views.Skins;
using Application = System.Windows.Application;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;

namespace Slimey.Views;

/// <summary>
/// 다크 2-pane 설정 UI(Clawd 스타일). 좌측 네비 + 우측 패널. DataContext = AppSettings 직접 바인딩.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SlimeWindow _slime;
    private readonly Dictionary<SlimeSkinKind, Border> _themeCards = new();
    private bool _capturingHotkey;

    public SettingsWindow(AppSettings settings, SlimeWindow slime)
    {
        _settings = settings;
        _slime = slime;
        InitializeComponent();
        DataContext = settings;
        BuildThemeCards();
        UpdateRebindText();
        UpdateAimKeyText();
        UpdateBilliardSection();
        _settings.PropertyChanged += (_, ev) => { if (ev.PropertyName == nameof(AppSettings.Skin)) UpdateBilliardSection(); };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDownCapture;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelGeneral == null) return; // InitializeComponent 도중 초기 선택 이벤트 무시
        int i = Nav.SelectedIndex;
        PanelGeneral.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelTheme.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelSound.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelShortcuts.Visibility = i == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnResetPosition(object sender, RoutedEventArgs e) => _slime.ResetPositionPublic();

    private void UpdateBilliardSection()
        => BilliardSection.Visibility = _settings.Skin == SlimeSkinKind.Billiard ? Visibility.Visible : Visibility.Collapsed;

    // ── 테마 카드(스킨 미리보기) ─────────────────────────────
    private static readonly (SlimeSkinKind kind, string name)[] Skins =
    {
        (SlimeSkinKind.Jelly, "슬라임"),
        (SlimeSkinKind.Billiard, "당구공"),
        (SlimeSkinKind.Pokeball, "몬스터볼"),
        (SlimeSkinKind.Ultra, "하이퍼볼"),
        (SlimeSkinKind.Master, "마스터볼"),
        (SlimeSkinKind.Basketball, "농구공"),
    };

    private void BuildThemeCards()
    {
        foreach (var (kind, name) in Skins)
        {
            var preview = MakeSkin(kind);
            preview.Width = 74;
            preview.Height = 74;

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new Border
            {
                Width = 96,
                Height = 82,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("WinBg"),
                Child = preview,
            });
            stack.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = (Brush)FindResource("TextBrush"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            });

            var card = new Border
            {
                Width = 116,
                Padding = new Thickness(8, 10, 8, 10),
                Margin = new Thickness(0, 0, 12, 12),
                CornerRadius = new CornerRadius(11),
                Background = (Brush)FindResource("CardBg"),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = stack,
                Tag = kind,
            };
            card.MouseLeftButtonUp += (_, _) => _settings.Skin = kind;
            _themeCards[kind] = card;
            ThemeCards.Children.Add(card);
        }
        HighlightSelectedSkin();
        _settings.PropertyChanged += (_, ev) => { if (ev.PropertyName == nameof(AppSettings.Skin)) HighlightSelectedSkin(); };
    }

    private static UserControl MakeSkin(SlimeSkinKind kind) => kind switch
    {
        SlimeSkinKind.Billiard => new BilliardSkin(),
        SlimeSkinKind.Pokeball or SlimeSkinKind.Ultra or SlimeSkinKind.Master => new BallSkin(kind),
        SlimeSkinKind.Basketball => new BasketballSkin(),
        _ => new JellySkin(),
    };

    private void HighlightSelectedSkin()
    {
        var accent = (Brush)FindResource("Accent");
        foreach (var (kind, card) in _themeCards)
            card.BorderBrush = kind == _settings.Skin ? accent : Brushes.Transparent;
    }

    // ── 잡기 단축키 재설정(변경→캡처→저장) ──────────────────
    private int _pMod, _pVk, _pMouse;

    private void OnRebindCatch(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        _pMod = _pVk = _pMouse = 0;
        RebindBtn.Content = "키/클릭 입력…";
        SaveBtn.IsEnabled = false;
    }

    private static int CurrentMods()
    {
        var m = Keyboard.Modifiers;
        return (m.HasFlag(ModifierKeys.Alt) ? 1 : 0)
             | (m.HasFlag(ModifierKeys.Control) ? 2 : 0)
             | (m.HasFlag(ModifierKeys.Shift) ? 4 : 0)
             | (m.HasFlag(ModifierKeys.Windows) ? 8 : 0);
    }

    // ── 농구공 조준 단축키(단일 키, 즉시 적용) ───────────────
    private bool _capturingAimKey;

    private void OnRebindAimKey(object sender, RoutedEventArgs e)
    {
        _capturingAimKey = true;
        AimKeyBtn.Content = "키 입력…";
    }

    private void UpdateAimKeyText()
        => AimKeyBtn.Content = _settings.BasketballAimVk == 0
            ? "(없음)"
            : KeyInterop.KeyFromVirtualKey(_settings.BasketballAimVk).ToString();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingAimKey)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            // 좌/우 수정자는 공용 VK 로 저장(Shift/Ctrl/Alt 를 좌우 구분 없이 인식)
            int vk = k switch
            {
                Key.LeftShift or Key.RightShift => 0x10,
                Key.LeftCtrl or Key.RightCtrl => 0x11,
                Key.LeftAlt or Key.RightAlt => 0x12,
                _ => KeyInterop.VirtualKeyFromKey(k),
            };
            _settings.BasketballAimVk = vk;
            _capturingAimKey = false;
            UpdateAimKeyText();
            e.Handled = true;
            return;
        }

        if (!_capturingHotkey) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; // 수정자 단독은 대기

        _pMod = CurrentMods();
        _pVk = KeyInterop.VirtualKeyFromKey(key);
        _pMouse = 0;
        EndCapture();
        e.Handled = true;
    }

    private void OnPreviewMouseDownCapture(object sender, MouseButtonEventArgs e)
    {
        if (!_capturingHotkey) return;
        _pMod = CurrentMods();
        _pMouse = e.ChangedButton switch { MouseButton.Right => 2, MouseButton.Middle => 3, _ => 1 };
        _pVk = 0;
        EndCapture();
        e.Handled = true;
    }

    private void EndCapture()
    {
        _capturingHotkey = false;
        RebindBtn.Content = HotkeyText(_pMod, _pVk, _pMouse);
        SaveBtn.IsEnabled = true; // 저장을 눌러야 적용
    }

    private void OnSaveHotkey(object sender, RoutedEventArgs e)
    {
        _settings.CatchHotkeyMod = _pMod;
        _settings.CatchHotkeyVk = _pVk;
        _settings.CatchHotkeyMouse = _pMouse;
        SaveBtn.IsEnabled = false;
    }

    private void UpdateRebindText()
    {
        RebindBtn.Content = HotkeyText(_settings.CatchHotkeyMod, _settings.CatchHotkeyVk, _settings.CatchHotkeyMouse);
    }

    private static string HotkeyText(int mod, int vk, int mouse)
    {
        var parts = new List<string>();
        if ((mod & 2) != 0) parts.Add("Ctrl");
        if ((mod & 4) != 0) parts.Add("Shift");
        if ((mod & 1) != 0) parts.Add("Alt");
        if ((mod & 8) != 0) parts.Add("Win");
        if (vk != 0) parts.Add(KeyInterop.KeyFromVirtualKey(vk).ToString());
        else if (mouse == 1) parts.Add("좌클릭");
        else if (mouse == 2) parts.Add("우클릭");
        else if (mouse == 3) parts.Add("중간클릭");
        return parts.Count > 0 ? string.Join(" + ", parts) : "(없음)";
    }
}
