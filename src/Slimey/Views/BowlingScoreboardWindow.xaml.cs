using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Slimey.Views;

internal sealed record BowlingFrameDisplay(
    string Roll1,
    string Roll2,
    string Roll3,
    int? Cumulative,
    bool Complete);

/// <summary>
/// 볼링장 천장 모니터를 본뜬 10프레임 점수판.
/// 주 모니터 우측 상단에 배치되며 클릭을 통과한다.
/// </summary>
public partial class BowlingScoreboardWindow : Window
{
    private const double DesignWidth = 760;
    private const double DesignHeight = 330;

    private readonly double _targetLeftPx;
    private readonly double _targetTopPx;
    private readonly double _panelWpx;
    private readonly double _panelHpx;
    private double _scaleX = 1;
    private double _scaleY = 1;

    private readonly Border[] _frameCells = new Border[10];
    private readonly TextBlock[] _frameNumbers = new TextBlock[10];
    private readonly TextBlock[] _roll1 = new TextBlock[10];
    private readonly TextBlock[] _roll2 = new TextBlock[10];
    private readonly TextBlock[] _roll3 = new TextBlock[10];
    private readonly TextBlock[] _cumulative = new TextBlock[10];
    private TextBlock _roundText = null!;
    private TextBlock _statusText = null!;
    private TextBlock _totalText = null!;
    private string? _lastSignature;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x8000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    public BowlingScoreboardWindow(Rect monitorArea)
    {
        InitializeComponent();

        double available = Math.Max(420, monitorArea.Width - 32);
        _panelWpx = Math.Min(available, Math.Clamp(monitorArea.Width * 0.43, 420, 760));
        _panelHpx = _panelWpx * DesignHeight / DesignWidth;
        _targetLeftPx = Math.Max(monitorArea.Left + 16, monitorArea.Right - _panelWpx - 18);
        _targetTopPx = monitorArea.Top + 14;

        Box.Child = BuildMonitor();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            Matrix m = src.CompositionTarget.TransformToDevice;
            _scaleX = m.M11 > 0 ? m.M11 : 1;
            _scaleY = m.M22 > 0 ? m.M22 : 1;
        }

        Left = _targetLeftPx / _scaleX;
        Top = _targetTopPx / _scaleY;
        Width = _panelWpx / _scaleX;
        Height = _panelHpx / _scaleY;
    }

    internal FrameworkElement DetachPreviewPanel()
    {
        var panel = (FrameworkElement)Box.Child;
        Box.Child = null;
        return panel;
    }

    internal void SetGame(
        int currentFrame,
        int currentThrow,
        int total,
        IReadOnlyList<BowlingFrameDisplay> frames,
        string status,
        Color statusColor,
        bool gameOver)
    {
        string signature = $"{currentFrame}|{currentThrow}|{total}|{status}|{gameOver}|"
            + string.Join(";", frames);
        if (_lastSignature == signature) return;
        _lastSignature = signature;

        _roundText.Text = gameOver
            ? "GAME COMPLETE"
            : $"{currentFrame} FRAME  ·  {currentThrow} BALL";
        _statusText.Text = status;
        _statusText.Foreground = Brush(statusColor);
        _totalText.Text = total.ToString();

        for (int i = 0; i < 10; i++)
        {
            BowlingFrameDisplay frame = i < frames.Count
                ? frames[i]
                : new BowlingFrameDisplay("", "", "", null, false);
            bool active = !gameOver && i == currentFrame - 1;

            _roll1[i].Text = frame.Roll1;
            _roll2[i].Text = frame.Roll2;
            _roll3[i].Text = frame.Roll3;
            _cumulative[i].Text = frame.Cumulative?.ToString() ?? "";

            _frameCells[i].Background = active
                ? VGradient(("#F7D76B", 0), ("#E9AD27", 1))
                : frame.Complete
                    ? VGradient(("#28577E", 0), ("#183B5C", 1))
                    : VGradient(("#173D60", 0), ("#102D49", 1));

            Color main = active ? C("#17283A") : C("#F7FAFC");
            Color muted = active ? C("#5D4213") : C("#9DB9CF");
            _frameNumbers[i].Foreground = Brush(active ? C("#7A4B08") : C("#BBD4E8"));
            _roll1[i].Foreground = Brush(main);
            _roll2[i].Foreground = Brush(main);
            _roll3[i].Foreground = Brush(main);
            _cumulative[i].Foreground = Brush(frame.Cumulative.HasValue ? main : muted);
        }
    }

    private FrameworkElement BuildMonitor()
    {
        var outer = new Border
        {
            Width = DesignWidth,
            Height = DesignHeight,
            CornerRadius = new CornerRadius(26),
            BorderThickness = new Thickness(6),
            BorderBrush = Brush(C("#11171B")),
            Background = VGradient(
                ("#848B8B", 0),
                ("#2A3032", 0.12),
                ("#13191B", 0.78),
                ("#E6E7E5", 0.80),
                ("#A6AAA8", 1)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 5,
                Opacity = 0.62,
            },
        };

        var shell = new Grid { Margin = new Thickness(12, 11, 12, 12) };
        outer.Child = shell;

        var screen = new Border
        {
            Margin = new Thickness(7, 7, 7, 46),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(4),
            BorderBrush = Brush(C("#070C10")),
            Background = VGradient(("#0B3558", 0), ("#071D31", 1)),
            Child = BuildScreen(),
        };
        shell.Children.Add(screen);

        var lowerBezel = new Border
        {
            Height = 39,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(18, 0, 18, 2),
            CornerRadius = new CornerRadius(0, 0, 13, 13),
            Background = VGradient(("#F6F6F2", 0), ("#B5BAB9", 0.68), ("#747B7D", 1)),
            BorderBrush = Brush(C("#30383A")),
            BorderThickness = new Thickness(1),
        };
        lowerBezel.Child = Text("SLIMEY  ·  BOWLING SYSTEM", 12, C("#4C5558"), FontWeights.SemiBold,
            HorizontalAlignment.Center, VerticalAlignment.Center);
        shell.Children.Add(lowerBezel);

        AddScrew(shell, HorizontalAlignment.Left, VerticalAlignment.Top, 12, 11);
        AddScrew(shell, HorizontalAlignment.Right, VerticalAlignment.Top, 11, 11);

        return outer;
    }

    private FrameworkElement BuildScreen()
    {
        var screen = new Grid();
        screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(148) });
        screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });

        var header = new Grid { Margin = new Thickness(14, 4, 14, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });

        var lane = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        lane.Children.Add(Text("1", 42, Colors.White, FontWeights.Light));
        lane.Children.Add(Text(" LANE", 14, C("#D7E9F5"), FontWeights.SemiBold,
            HorizontalAlignment.Left, VerticalAlignment.Center));
        Grid.SetColumn(lane, 0);
        header.Children.Add(lane);

        var title = Text("SLIMEY BOWLING", 20, C("#EAF5FD"), FontWeights.Bold,
            HorizontalAlignment.Center, VerticalAlignment.Center);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        _roundText = Text("1 FRAME  ·  1 BALL", 14, C("#80C8FA"), FontWeights.SemiBold,
            HorizontalAlignment.Right, VerticalAlignment.Center);
        Grid.SetColumn(_roundText, 2);
        header.Children.Add(_roundText);
        screen.Children.Add(header);

        var table = BuildTable();
        Grid.SetRow(table, 1);
        screen.Children.Add(table);

        var footer = new Border
        {
            Margin = new Thickness(14, 5, 14, 8),
            CornerRadius = new CornerRadius(4),
            Background = Brush(Color.FromArgb(0x70, 0x04, 0x16, 0x26)),
            BorderBrush = Brush(Color.FromArgb(0x66, 0x72, 0x9A, 0xB9)),
            BorderThickness = new Thickness(1),
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(134) });

        _statusText = Text("READY · 공을 굴려주세요", 16, C("#A9D9F7"), FontWeights.SemiBold,
            HorizontalAlignment.Left, VerticalAlignment.Center);
        _statusText.Margin = new Thickness(14, 0, 0, 0);
        footerGrid.Children.Add(_statusText);

        var totalBadge = new Border
        {
            Margin = new Thickness(5),
            CornerRadius = new CornerRadius(4),
            Background = HGradient(("#B20C43", 0), ("#7D153A", 1)),
            BorderBrush = Brush(C("#DE4770")),
            BorderThickness = new Thickness(1),
        };
        var totalStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        totalStack.Children.Add(Text("◆", 17, C("#FF2E72"), FontWeights.Bold));
        _totalText = Text("0", 25, Colors.White, FontWeights.SemiBold);
        _totalText.Margin = new Thickness(10, 0, 0, 0);
        totalStack.Children.Add(_totalText);
        totalBadge.Child = totalStack;
        Grid.SetColumn(totalBadge, 1);
        footerGrid.Children.Add(totalBadge);

        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        screen.Children.Add(footer);
        return screen;
    }

    private FrameworkElement BuildTable()
    {
        var table = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        for (int i = 0; i < 9; i++)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

        var player = new Border
        {
            BorderBrush = Brush(C("#527A99")),
            BorderThickness = new Thickness(1),
            Background = VGradient(("#173E61", 0), ("#0D2B46", 1)),
        };
        var playerStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0),
        };
        playerStack.Children.Add(Text("GAME 1", 12, C("#91B8D4"), FontWeights.SemiBold));
        playerStack.Children.Add(Text("SLIMEY", 17, Colors.White, FontWeights.Bold));
        playerStack.Children.Add(Text("10 FRAME", 11, C("#F5C851"), FontWeights.SemiBold));
        player.Child = playerStack;
        table.Children.Add(player);

        for (int i = 0; i < 10; i++)
        {
            Border cell = BuildFrameCell(i);
            Grid.SetColumn(cell, i + 1);
            table.Children.Add(cell);
        }
        return table;
    }

    private Border BuildFrameCell(int index)
    {
        var cell = new Border
        {
            BorderBrush = Brush(C("#527A99")),
            BorderThickness = new Thickness(0, 1, 1, 1),
            Background = VGradient(("#173D60", 0), ("#102D49", 1)),
        };
        _frameCells[index] = cell;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(43) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _frameNumbers[index] = Text((index + 1).ToString(), 17, C("#BBD4E8"), FontWeights.SemiBold,
            HorizontalAlignment.Center, VerticalAlignment.Center);
        grid.Children.Add(_frameNumbers[index]);

        var rolls = new Grid
        {
            Background = Brush(Color.FromArgb(0x38, 0x89, 0xB2, 0xD0)),
        };
        int rollColumns = index == 9 ? 3 : 2;
        for (int i = 0; i < rollColumns; i++)
            rolls.ColumnDefinitions.Add(new ColumnDefinition());

        _roll1[index] = MarkText();
        _roll2[index] = MarkText();
        _roll3[index] = MarkText();
        rolls.Children.Add(_roll1[index]);
        Grid.SetColumn(_roll2[index], 1);
        rolls.Children.Add(_roll2[index]);
        if (rollColumns == 3)
        {
            Grid.SetColumn(_roll3[index], 2);
            rolls.Children.Add(_roll3[index]);
        }
        Grid.SetRow(rolls, 1);
        grid.Children.Add(rolls);

        _cumulative[index] = Text("", 23, Colors.White, FontWeights.SemiBold,
            HorizontalAlignment.Center, VerticalAlignment.Center);
        Grid.SetRow(_cumulative[index], 2);
        grid.Children.Add(_cumulative[index]);

        cell.Child = grid;
        return cell;
    }

    private static TextBlock MarkText()
        => Text("", 22, Colors.White, FontWeights.Bold,
            HorizontalAlignment.Center, VerticalAlignment.Center);

    private static TextBlock Text(
        string text,
        double size,
        Color color,
        FontWeight weight,
        HorizontalAlignment horizontal = HorizontalAlignment.Left,
        VerticalAlignment vertical = VerticalAlignment.Center)
        => new()
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            FontWeight = weight,
            Foreground = Brush(color),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
        };

    private static void AddScrew(
        Grid root,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        double x,
        double y)
    {
        root.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Brush(C("#202628")),
            Stroke = Brush(C("#A8AEAD")),
            StrokeThickness = 1,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Margin = new Thickness(x, y, x, y),
        });
    }

    private static Brush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush VGradient(params (string hex, double offset)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        foreach (var (hex, offset) in stops)
            brush.GradientStops.Add(new GradientStop(C(hex), offset));
        brush.Freeze();
        return brush;
    }

    private static Brush HGradient(params (string hex, double offset)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        foreach (var (hex, offset) in stops)
            brush.GradientStops.Add(new GradientStop(C(hex), offset));
        brush.Freeze();
        return brush;
    }

    private static Color C(string hex)
        => (Color)ColorConverter.ConvertFromString(hex);
}
