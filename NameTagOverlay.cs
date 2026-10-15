using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FaceSt;

internal sealed class NameTagOverlay : Window
{
    private readonly Canvas _canvas = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Dictionary<int, TagVisual> _tags = new();
    private readonly string _snapshot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FaceSt", "Captures", "nametags.json");
    private DateTime _lastSnapshotTime;
    private DateTime _lastReadTime;
    private DateTime _lastGameSearch;
    private IntPtr _gameWindow;
    private uint _gamePid;

    private sealed class TagVisual
    {
        public required FrameworkElement Element { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double TargetX { get; set; }
        public double TargetY { get; set; }
        public bool Seen { get; set; }
    }

    public NameTagOverlay()
    {
        Title = "FaceSt İsim Etiketleri";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Width = Height = 1;
        Content = _canvas;
        SourceInitialized += (_, _) => MakeClickThrough();
        _timer.Tick += (_, _) => UpdateOverlay();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        const int index = -20;
        var current = GetWindowLongPtr(hwnd, index).ToInt64();
        SetWindowLongPtr(hwnd, index, new IntPtr(current | 0x20 | 0x80 | 0x08000000));
    }

    private void UpdateOverlay()
    {
        if (DateTime.UtcNow - _lastGameSearch > TimeSpan.FromSeconds(2))
        {
            _lastGameSearch = DateTime.UtcNow;
            FindGameWindow();
        }
        if (_gameWindow == IntPtr.Zero || !GetClientRect(_gameWindow, out var client) ||
            client.Right <= 0 || client.Bottom <= 0 || !IsGameForeground())
        {
            Visibility = Visibility.Hidden;
            return;
        }

        var point = new NativePoint();
        if (!ClientToScreen(_gameWindow, ref point)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;
        var x = point.X / scaleX;
        var y = point.Y / scaleY;
        var width = client.Right / scaleX;
        var height = client.Bottom / scaleY;
        if (Math.Abs(Left - x) > 0.5 || Math.Abs(Top - y) > 0.5 ||
            Math.Abs(Width - width) > 0.5 || Math.Abs(Height - height) > 0.5)
        {
            Left = x;
            Top = y;
            Width = width;
            Height = height;
        }
        if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;

        if (DateTime.UtcNow - _lastReadTime > TimeSpan.FromMilliseconds(120))
        {
            _lastReadTime = DateTime.UtcNow;
            ReadSnapshot();
        }
        if (DateTime.UtcNow - _lastSnapshotTime > TimeSpan.FromSeconds(1))
        {
            ClearTags();
            return;
        }
        foreach (var tag in _tags.Values)
        {
            tag.X += (tag.TargetX - tag.X) * 0.55;
            tag.Y += (tag.TargetY - tag.Y) * 0.55;
            Canvas.SetLeft(tag.Element, tag.X * width - tag.Element.ActualWidth / 2);
            Canvas.SetTop(tag.Element, tag.Y * height - tag.Element.ActualHeight - 5);
        }
    }

    private void ReadSnapshot()
    {
        try
        {
            var file = new FileInfo(_snapshot);
            if (!file.Exists || file.LastWriteTimeUtc <= _lastSnapshotTime) return;
            using var json = JsonDocument.Parse(File.ReadAllText(_snapshot));
            var root = json.RootElement;
            if (root.GetProperty("schema").GetString() != "facest.nametags.v1") return;
            foreach (var tag in _tags.Values) tag.Seen = false;
            var players = root.GetProperty("players");
            if (players.GetArrayLength() > 64) return;
            foreach (var player in players.EnumerateArray())
            {
                var id = player.GetProperty("serverId").GetInt32();
                var x = player.GetProperty("x").GetDouble();
                var y = player.GetProperty("y").GetDouble();
                var name = player.GetProperty("displayName").GetString() ?? "Oyuncu";
                if (id is < 1 or > 65535 || !double.IsFinite(x) || !double.IsFinite(y) ||
                    x is < 0 or > 1 || y is < 0 or > 1) continue;
                name = new string(name.Where(c => !char.IsControl(c)).Take(32).ToArray()).Trim();
                if (name.Length == 0) name = "Oyuncu";
                if (!_tags.TryGetValue(id, out var tag))
                {
                    var element = CreateLabel(name, id);
                    tag = new TagVisual { Element = element, X = x, Y = y };
                    _tags.Add(id, tag);
                    _canvas.Children.Add(element);
                }
                tag.TargetX = x;
                tag.TargetY = y;
                tag.Seen = true;
            }
            foreach (var id in _tags.Where(item => !item.Value.Seen).Select(item => item.Key).ToArray())
            {
                _canvas.Children.Remove(_tags[id].Element);
                _tags.Remove(id);
            }
            _lastSnapshotTime = file.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
        }
    }

    private static FrameworkElement CreateLabel(string name, int id)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Effect = new DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 1
        };
        panel.Children.Add(new TextBlock
        {
            Text = $"(( {name} )) ", FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"[{id}]", FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 140, 220))
        });
        return panel;
    }

    private void ClearTags()
    {
        _canvas.Children.Clear();
        _tags.Clear();
    }

    private void FindGameWindow()
    {
        _gameWindow = IntPtr.Zero;
        _gamePid = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                if (!name.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith("_GTAProcess", StringComparison.OrdinalIgnoreCase)) continue;
                var pid = (uint)process.Id;
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out var windowPid);
                    if (windowPid != pid || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero ||
                        !GetClientRect(hwnd, out var bounds) || bounds.Right < 640 || bounds.Bottom < 400)
                        return true;
                    _gameWindow = hwnd;
                    _gamePid = pid;
                    return false;
                }, IntPtr.Zero);
                if (_gameWindow != IntPtr.Zero) break;
            }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }

    private bool IsGameForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == _gamePid;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint
    {
        public int X, Y;
    }
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
