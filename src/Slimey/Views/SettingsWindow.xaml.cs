using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Slimey.Models;
using Slimey.Network;
using Slimey.Services;
using Slimey.Views.Skins;
using Application = System.Windows.Application;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;

namespace Slimey.Views;

/// <summary>
/// 다크 2-pane 설정 UI(Clawd 스타일). 좌측 네비 + 우측 패널. DataContext = AppSettings 직접 바인딩.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SlimeWindow _slime;
    private readonly Dictionary<SlimeSkinKind, Border> _themeCards = new();
    private readonly Dictionary<SlimeSkinKind, Border> _themePreviewHosts = new();

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
        UpdateCustomImageSection();
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDownCapture;

        _slime.RelayStateChanged += st => Dispatcher.Invoke(() => UpdateNetStatus(st));
        BuildLinkRows();
        RefreshNetworkPanel();

        Closed += (_, _) => _settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Skin):
                HighlightSelectedSkin();
                UpdateBilliardSection();
                UpdateCustomImageSection();
                break;
            case nameof(AppSettings.SkinImages):
            case nameof(AppSettings.SkinImageEnabled):
            case nameof(AppSettings.SkinImageScale):
                RefreshThemePreviews();
                UpdateCustomImageSection();
                break;
        }
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
        PanelNetwork.Visibility = i == 4 ? Visibility.Visible : Visibility.Collapsed;
        if (i == 4) RefreshNetworkPanel();
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
        (SlimeSkinKind.Bowling, "볼링공"),
    };

    private void BuildThemeCards()
    {
        foreach (var (kind, name) in Skins)
        {
            var previewHost = new Border
            {
                Width = 96,
                Height = 82,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("WinBg"),
                Child = MakePreviewVisual(kind, 74),
            };
            _themePreviewHosts[kind] = previewHost;

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(previewHost);
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
    }

    private static UserControl MakeSkin(SlimeSkinKind kind) => kind switch
    {
        SlimeSkinKind.Billiard => new BilliardSkin(),
        SlimeSkinKind.Pokeball or SlimeSkinKind.Ultra or SlimeSkinKind.Master => new BallSkin(kind),
        SlimeSkinKind.Basketball => new BasketballSkin(),
        SlimeSkinKind.Bowling => new BowlingSkin(),
        _ => new JellySkin(),
    };

    /// <summary>커스텀 이미지가 있으면 겹쳐 보여주는 미리보기(실제 공과 같은 구성).
    /// SlimeWindow 의 덧씌우기 레이어와 같은 96 디자인 좌표·원형 클립을 쓴다.</summary>
    private FrameworkElement MakePreviewVisual(SlimeSkinKind kind, double size)
    {
        var design = new Grid { Width = 96, Height = 96 };
        design.Children.Add(MakeSkin(kind));

        var img = _settings.SkinImageEnabled && SkinImageStore.Supports(kind)
            ? SkinImageStore.Load(kind)
            : null;
        if (img != null)
        {
            double d = 84.0 * System.Math.Clamp(_settings.SkinImageScale, 0.2, 2.0);
            var layer = new Grid
            {
                Clip = new EllipseGeometry(new Point(48, 48), 42, 42),
                IsHitTestVisible = false,
            };
            layer.Children.Add(new Image
            {
                Source = img,
                Width = d,
                Height = d,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            layer.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Margin = new Thickness(6),
                Fill = SphereShadeBrush(),
            });
            design.Children.Add(layer);
        }

        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = design };
    }

    /// <summary>덧씌운 이미지가 스티커가 아니라 공 표면처럼 보이게 하는 가장자리 음영.</summary>
    private static Brush SphereShadeBrush()
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.62));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0, 0, 0), 0.88));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x6E, 0, 0, 0), 1.0));
        b.Freeze();
        return b;
    }

    private void RefreshThemePreviews()
    {
        foreach (var (kind, host) in _themePreviewHosts)
            host.Child = MakePreviewVisual(kind, 74);
    }

    private void HighlightSelectedSkin()
    {
        var accent = (Brush)FindResource("Accent");
        foreach (var (kind, card) in _themeCards)
            card.BorderBrush = kind == _settings.Skin ? accent : Brushes.Transparent;
    }

    // ── 테마별 커스텀 이미지 ────────────────────────────────
    /// <summary>현재 선택 테마 이름(설정창 표시용).</summary>
    private string CurrentThemeName()
        => Skins.FirstOrDefault(s => s.kind == _settings.Skin).name ?? _settings.Skin.ToString();

    private void UpdateCustomImageSection()
    {
        var kind = _settings.Skin;
        bool supported = SkinImageStore.Supports(kind);
        CustomImageSection.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        if (!supported) return;

        CustomImageHeader.Text = $"커스텀 이미지 — {CurrentThemeName()}";

        bool has = SkinImageStore.Has(kind);
        _settings.SkinImages.TryGetValue(kind.ToString(), out string? name);
        CustomImageName.Text = has
            ? (string.IsNullOrWhiteSpace(name) ? "(직접 그린 이미지)" : name)
            : "(없음)";
        RemoveImageBtn.IsEnabled = has;

        CustomPreviewHost.Children.Clear();
        CustomPreviewHost.Children.Add(MakePreviewVisual(kind, 84));
    }

    /// <summary>SkinImages 기록을 갱신하고 화면·저장에 반영한다.</summary>
    private void SetSkinImageRecord(SlimeSkinKind kind, string? displayName)
    {
        string key = kind.ToString();
        if (displayName == null) _settings.SkinImages.Remove(key);
        else _settings.SkinImages[key] = displayName;
        _settings.NotifySkinImagesChanged(); // 공·미리보기 갱신 + 디바운스 자동 저장
    }

    private void OnLoadSkinImage(object sender, RoutedEventArgs e)
    {
        var kind = _settings.Skin;
        var dlg = new OpenFileDialog
        {
            Title = $"{CurrentThemeName()} 에 씌울 이미지 선택",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|모든 파일|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        if (!SkinImageStore.Import(kind, dlg.FileName))
        {
            MessageBox.Show(this, "이미지를 불러오지 못했습니다. 다른 파일로 시도해 보세요.", "Slimey",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SetSkinImageRecord(kind, System.IO.Path.GetFileName(dlg.FileName));
    }

    private void OnDrawSkinImage(object sender, RoutedEventArgs e)
    {
        var kind = _settings.Skin;
        var win = new SkinDrawWindow(kind, CurrentThemeName()) { Owner = this };
        win.ShowDialog();
        // 빈 문자열 = 이미지는 있으나 원본 파일이 없음(직접 그린 것)
        if (win.Saved) SetSkinImageRecord(kind, "");
    }

    private void OnRemoveSkinImage(object sender, RoutedEventArgs e)
    {
        var kind = _settings.Skin;
        SkinImageStore.Remove(kind);
        SetSkinImageRecord(kind, null);
    }

    // ── 단축키 재설정(변경→캡처→저장) ───────────────────────
    /// <summary>어떤 단축키를 캡처 중인가. None 이면 캡처 중 아님.</summary>
    private enum HotkeyTarget { None, Catch, Hide }

    private HotkeyTarget _capturing = HotkeyTarget.None;
    private int _pMod, _pVk, _pMouse;            // 잡기 대기값
    private int _hMod, _hVk, _hMouse;            // 숨기기 대기값

    private void OnRebindCatch(object sender, RoutedEventArgs e)
    {
        _capturing = HotkeyTarget.Catch;
        _pMod = _pVk = _pMouse = 0;
        RebindBtn.Content = "키/클릭 입력…";
        SaveBtn.IsEnabled = false;
    }

    private void OnRebindHide(object sender, RoutedEventArgs e)
    {
        _capturing = HotkeyTarget.Hide;
        _hMod = _hVk = _hMouse = 0;
        RebindHideBtn.Content = "키/클릭 입력…";
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
            int aimVk = k switch
            {
                Key.LeftShift or Key.RightShift => 0x10,
                Key.LeftCtrl or Key.RightCtrl => 0x11,
                Key.LeftAlt or Key.RightAlt => 0x12,
                _ => KeyInterop.VirtualKeyFromKey(k),
            };
            _settings.BasketballAimVk = aimVk;
            _capturingAimKey = false;
            UpdateAimKeyText();
            e.Handled = true;
            return;
        }

        if (_capturing == HotkeyTarget.None) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; // 수정자 단독은 대기

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (_capturing == HotkeyTarget.Catch) { _pMod = CurrentMods(); _pVk = vk; _pMouse = 0; }
        else { _hMod = CurrentMods(); _hVk = vk; _hMouse = 0; }
        EndCapture();
        e.Handled = true;
    }

    private void OnPreviewMouseDownCapture(object sender, MouseButtonEventArgs e)
    {
        if (_capturing == HotkeyTarget.None) return;
        int btn = e.ChangedButton switch { MouseButton.Right => 2, MouseButton.Middle => 3, _ => 1 };
        if (_capturing == HotkeyTarget.Catch) { _pMod = CurrentMods(); _pMouse = btn; _pVk = 0; }
        else { _hMod = CurrentMods(); _hMouse = btn; _hVk = 0; }
        EndCapture();
        e.Handled = true;
    }

    private void EndCapture()
    {
        if (_capturing == HotkeyTarget.Catch) RebindBtn.Content = HotkeyText(_pMod, _pVk, _pMouse);
        else RebindHideBtn.Content = HotkeyText(_hMod, _hVk, _hMouse);
        _capturing = HotkeyTarget.None;
        SaveBtn.IsEnabled = true; // 저장을 눌러야 적용
    }

    private void OnSaveHotkey(object sender, RoutedEventArgs e)
    {
        _settings.CatchHotkeyMod = _pMod;
        _settings.CatchHotkeyVk = _pVk;
        _settings.CatchHotkeyMouse = _pMouse;
        _settings.HideHotkeyMod = _hMod;
        _settings.HideHotkeyVk = _hVk;
        _settings.HideHotkeyMouse = _hMouse;
        SaveBtn.IsEnabled = false;
    }

    private void UpdateRebindText()
    {
        // 대기값을 현재 설정으로 초기화 — 한쪽만 바꿔 저장해도 다른 쪽이 지워지지 않도록.
        _pMod = _settings.CatchHotkeyMod; _pVk = _settings.CatchHotkeyVk; _pMouse = _settings.CatchHotkeyMouse;
        _hMod = _settings.HideHotkeyMod; _hVk = _settings.HideHotkeyVk; _hMouse = _settings.HideHotkeyMouse;

        RebindBtn.Content = HotkeyText(_pMod, _pVk, _pMouse);
        RebindHideBtn.Content = HotkeyText(_hMod, _hVk, _hMouse);
    }

    private static string HotkeyText(int mod, int vk, int mouse)
    {
        var parts = new List<string>();
        if ((mod & 2) != 0) parts.Add("Ctrl");
        if ((mod & 4) != 0) parts.Add("Shift");
        if ((mod & 1) != 0) parts.Add("Alt");
        if ((mod & 8) != 0) parts.Add("Win");
        if (vk != 0) parts.Add(KeyDisplayName(vk));
        else if (mouse == 1) parts.Add("좌클릭");
        else if (mouse == 2) parts.Add("우클릭");
        else if (mouse == 3) parts.Add("중간클릭");
        return parts.Count > 0 ? string.Join(" + ", parts) : "(없음)";
    }

    /// <summary>Key.Oem3 처럼 알아보기 어려운 이름을 실제 새겨진 글자로 바꿔 보여준다.</summary>
    private static string KeyDisplayName(int vk) => vk switch
    {
        0xC0 => "`",   // Oem3 (물결/백틱)
        0xBD => "-",   // OemMinus
        0xBB => "=",   // OemPlus
        0xDB => "[",   // Oem4
        0xDD => "]",   // Oem6
        0xDC => "\\",  // Oem5
        0xBA => ";",   // Oem1
        0xDE => "'",   // Oem7
        0xBC => ",",   // OemComma
        0xBE => ".",   // OemPeriod
        0xBF => "/",   // Oem2
        _ => KeyInterop.KeyFromVirtualKey(vk).ToString(),
    };

    // ── 멀티 PC(릴레이) 설정 ────────────────────────────────
    private sealed class LinkRowUi
    {
        public Edge SelfEdge;
        public TextBox Target = null!;
        public ComboBox TargetEdge = null!;
        public CheckBox Flip = null!;
    }

    private readonly List<LinkRowUi> _linkRowsUi = new();

    private static readonly (Edge edge, string label)[] SelfEdges =
    {
        (Edge.Left, "왼쪽"), (Edge.Right, "오른쪽"), (Edge.Top, "위"), (Edge.Bottom, "아래"),
    };
    private static readonly (Edge edge, string label)[] AllEdges =
    {
        (Edge.Left, "왼쪽"), (Edge.Right, "오른쪽"), (Edge.Top, "위"), (Edge.Bottom, "아래"),
    };

    private void BuildLinkRows()
    {
        LinkRows.Children.Clear();
        _linkRowsUi.Clear();
        foreach (var (edge, label) in SelfEdges)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

            var lbl = new TextBlock { Text = label, Style = (Style)FindResource("RowLabel"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);

            var target = new TextBox { Style = (Style)FindResource("DarkTextBox"), VerticalAlignment = VerticalAlignment.Center };
            target.SetValue(FrameworkElement.ToolTipProperty, "상대 PC 이름(비우면 이 방향은 벽에 튕김)");
            Grid.SetColumn(target, 1);

            var arrow = new TextBlock { Text = "→", Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(arrow, 2);

            var combo = new ComboBox { Style = (Style)FindResource("DarkCombo"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var (e, l) in AllEdges)
                combo.Items.Add(new ComboBoxItem { Content = l, Tag = e.ToString() });
            Grid.SetColumn(combo, 3);

            var flipPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            flipPanel.Children.Add(new TextBlock { Text = "거울", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var flip = new CheckBox { Style = (Style)FindResource("Toggle"), VerticalAlignment = VerticalAlignment.Center };
            flip.SetValue(FrameworkElement.ToolTipProperty, "진입 위치를 좌우(상하) 반전");
            flipPanel.Children.Add(flip);
            Grid.SetColumn(flipPanel, 4);

            grid.Children.Add(lbl);
            grid.Children.Add(target);
            grid.Children.Add(arrow);
            grid.Children.Add(combo);
            grid.Children.Add(flipPanel);
            LinkRows.Children.Add(grid);

            _linkRowsUi.Add(new LinkRowUi { SelfEdge = edge, Target = target, TargetEdge = combo, Flip = flip });
        }
    }

    private void RefreshNetworkPanel()
    {
        var a = _slime.RelayAuth;
        NetEnabled.IsChecked = a.Enabled;
        NetServer.Text = a.ServerBaseUrl;
        NetRoom.Text = a.RoomCode;
        NetSecret.Password = a.Secret;
        NetNode.Text = a.NodeId;

        var links = _slime.CurrentLinks;
        foreach (var row in _linkRowsUi)
        {
            var match = links.FirstOrDefault(l =>
                l.From == a.NodeId && HandoffMath.TryParseEdge(l.FromEdge, out var e) && e == row.SelfEdge);
            if (match != null)
            {
                row.Target.Text = match.To;
                SelectEdge(row.TargetEdge, match.ToEdge);
                row.Flip.IsChecked = match.Flip;
            }
            else
            {
                row.Target.Text = "";
                SelectEdge(row.TargetEdge, OppositeName(row.SelfEdge));
                row.Flip.IsChecked = false;
            }
        }
        UpdateNetStatus(_slime.RelayState);
    }

    private void OnSaveNetwork(object sender, RoutedEventArgs e)
    {
        var a = _slime.RelayAuth;
        a.Enabled = NetEnabled.IsChecked == true;
        a.ServerBaseUrl = NetServer.Text.Trim();
        a.RoomCode = NetRoom.Text.Trim();
        a.Secret = NetSecret.Password;
        if (!string.IsNullOrWhiteSpace(NetNode.Text)) a.NodeId = NetNode.Text.Trim();
        _slime.ApplyRelaySettings(); // 저장 + 재연결

        // 이 PC의 나가는 링크를 구성해 기존(다른 PC) 링크와 합쳐 방 전체에 배포.
        var others = _slime.CurrentLinks.Where(l => l.From != a.NodeId).ToList();
        var mine = new List<EdgeLinkDto>();
        foreach (var row in _linkRowsUi)
        {
            string to = row.Target.Text.Trim();
            if (string.IsNullOrEmpty(to)) continue;
            string toEdge = (row.TargetEdge.SelectedItem as ComboBoxItem)?.Tag as string ?? "Left";
            mine.Add(new EdgeLinkDto
            {
                From = a.NodeId, FromEdge = row.SelfEdge.ToString(),
                To = to, ToEdge = toEdge, Flip = row.Flip.IsChecked == true,
            });
        }
        _slime.PushRoomConfig(others.Concat(mine).ToList());
        UpdateNetStatus(_slime.RelayState);
    }

    private void UpdateNetStatus(RelayState st)
    {
        if (NetStatus == null) return;
        NetStatus.Text = st switch
        {
            RelayState.Connected => "연결됨 ✓",
            RelayState.Connecting => "연결 중…",
            RelayState.Reconnecting => "재연결 중…",
            RelayState.Failed => "연결 실패 — 서버 주소를 확인하세요",
            _ => "꺼짐",
        };
        NetStatus.Foreground = (Brush)FindResource(st == RelayState.Connected ? "TextBrush" : "MutedBrush");
    }

    private static void SelectEdge(ComboBox combo, string edgeName)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if ((item.Tag as string) == edgeName) { combo.SelectedItem = item; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string OppositeName(Edge e) => e switch
    {
        Edge.Left => "Right",
        Edge.Right => "Left",
        Edge.Top => "Bottom",
        _ => "Top",
    };
}
