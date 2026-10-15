using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FaceSt.Core;

namespace FaceSt;

public partial class MainWindow : Window
{
    private readonly string _home = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaceSt");
    private bool _busy;
    private bool _injected;
    private bool _tagsOn;
    private NameTagOverlay? _tagOverlay;
    private string? _lastCode;
    private string? _lastXml;
    private string? _lastFile;
    private bool _showXml;
    private readonly System.Windows.Threading.DispatcherTimer _disclaimerTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };
    private Stopwatch? _disclaimerClock;

    private sealed record NearbyPlayer(int ServerId, string DisplayName, float DistanceMeters, string Sex);

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RoundCorners();
        UpdateGameStatus();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => UpdateGameStatus();
        timer.Start();
        _disclaimerTimer.Tick += (_, _) => UpdateDisclaimerCountdown();
        Loaded += (_, _) =>
        {
            _disclaimerClock = Stopwatch.StartNew();
            UpdateDisclaimerCountdown();
            _disclaimerTimer.Start();
        };
        Closed += (_, _) => _disclaimerTimer.Stop();
        Closing += (_, _) => StopTagsOnExit();
    }

    private void UpdateDisclaimerCountdown()
    {
        if (_disclaimerClock is null) return;
        var seconds = Math.Max(0, (int)Math.Ceiling(10 - _disclaimerClock.Elapsed.TotalSeconds));
        if (seconds > 0)
        {
            AcceptButton.Content = $"Lütfen bekleyin... ({seconds} sn)";
            return;
        }

        _disclaimerTimer.Stop();
        AcceptButton.Content = "Devam et";
        AcceptButton.IsEnabled = true;
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void RoundCorners()
    {
        var rounded = 2;
        DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,
            33, ref rounded, sizeof(int));
    }

    private async void AcceptDisclaimerClick(object sender, RoutedEventArgs e)
    {
        if (_disclaimerClock is null || _disclaimerClock.Elapsed < TimeSpan.FromSeconds(10)) return;
        DisclaimerPage.Visibility = Visibility.Collapsed;
        WorkbenchPage.Visibility = Visibility.Visible;

        var readerStatus = "UNLOADED";
        if (GameIsRunning())
        {
            try
            {
                var runtime = await Task.Run(PrepareRuntime);
                readerStatus = await RunCommandAsync(runtime, "--status");
                _injected = readerStatus == "LOADED";
            }
            catch { _injected = false; }
        }

        UpdateReaderState();
        if (readerStatus == "INCOMPATIBLE")
            SetStatus("FiveM'de eski okuyucu yüklü. Oyunu kapatıp yeniden açın; sonra INJECT'e basın.", true);
        else
            SetStatus(_injected
                ? "Yüz okuyucu FiveM'de aktif. Yakındakileri bulabilir veya ID girip yüz kodunu çıkarabilirsiniz."
                : "Önce INJECT ile yüz okuyucusunu oyuna yükleyin.", false);
    }

    private async void InjectClick(object sender, RoutedEventArgs e)
    {
        if (!CanStart()) return;
        _busy = true;
        SetBusyButtons();
        SetStatus("Yüz okuyucu FiveM'e yükleniyor...", false);
        try
        {
            var runtime = await Task.Run(PrepareRuntime);
            string response;
            Hide();
            try
            {
                FocusFiveM();
                await Task.Delay(300);
                response = await RunCommandAsync(runtime, "--inject");
            }
            finally { Show(); Activate(); }

            if (response != "LOADED") throw new InvalidOperationException("Yükleme doğrulanamadı.");
            _injected = true;
            SetStatus("Okuyucu yüklendi. Yakındakileri bulabilir veya ID girip yüz kodunu çıkarabilirsiniz.", false);
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { _busy = false; UpdateReaderState(); }
    }

    private async void UninjectClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!GameIsRunning())
        {
            _injected = false;
            CloseTagOverlay();
            UpdateReaderState();
            SetStatus("FiveM kapalı. Okuyucu artık oyunda değil.", false);
            return;
        }
        _busy = true;
        SetBusyButtons();
        SetStatus("Yüz okuyucu kaldırılıyor...", false);
        try
        {
            var runtime = await Task.Run(PrepareRuntime);
            var response = await RunCommandAsync(runtime, "--stop");
            if (response != "UNLOADED") throw new InvalidOperationException("Kaldırma doğrulanamadı.");
            _injected = false;
            CloseTagOverlay();
            SetStatus("Yüz okuyucu FiveM'den başarıyla kaldırıldı.", false);
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { _busy = false; UpdateReaderState(); }
    }

    private void SetBusyButtons()
    {
        InjectButton.IsEnabled = UninjectButton.IsEnabled =
            FindButton.IsEnabled = CaptureButton.IsEnabled = false;
        TagsButton.IsEnabled = false;
    }

    private void UpdateReaderState()
    {
        ReaderStatus.Text = _injected ? "●  OKUYUCU YÜKLÜ" : "○  OKUYUCU YÜKLENMEDİ";
        ReaderStatus.Foreground = (Brush)FindResource(_injected ? "Success" : "Muted");
        InjectButton.IsEnabled = !_injected;
        UninjectButton.IsEnabled = _injected;
        FindButton.IsEnabled = CaptureButton.IsEnabled = _injected;
        TagsButton.IsEnabled = _injected;
        TagsButton.Content = _tagsOn ? "İSİMLER: AÇIK" : "İSİMLER: KAPALI";
        TagsButton.Style = (Style)FindResource(_tagsOn ? "PrimaryButton" : "SecondaryButton");
    }

    private async void ToggleTagsClick(object sender, RoutedEventArgs e)
    {
        if (_busy || !_injected) return;
        _busy = true;
        SetBusyButtons();
        string? runtime = null;
        var startedTags = false;
        try
        {
            runtime = await Task.Run(PrepareRuntime);
            var response = await RunCommandAsync(runtime, _tagsOn ? "--tags-off" : "--tags-on");
            if (response != (_tagsOn ? "TAGS_OFF" : "TAGS_ON"))
                throw new InvalidOperationException("İsim etiketleri doğrulanamadı.");
            if (_tagsOn)
            {
                CloseTagOverlay();
                SetStatus("İsim etiketleri kapatıldı.", false);
            }
            else
            {
                startedTags = true;
                _tagOverlay = new NameTagOverlay();
                _tagOverlay.Show();
                _tagsOn = true;
                SetStatus("İsim etiketleri açık. FiveM öndeyken görünür.", false);
            }
        }
        catch (Exception ex)
        {
            if (startedTags && runtime is not null)
            {
                try { await RunCommandAsync(runtime, "--tags-off"); }
                catch { }
                CloseTagOverlay();
            }
            SetStatus(ex.Message, true);
        }
        finally { _busy = false; UpdateReaderState(); }
    }

    private void CloseTagOverlay()
    {
        _tagOverlay?.Close();
        _tagOverlay = null;
        _tagsOn = false;
    }

    private void StopTagsOnExit()
    {
        if (!_tagsOn) return;
        CloseTagOverlay();
        try
        {
            var runtime = PrepareRuntime();
            Process.Start(new ProcessStartInfo(Path.Combine(runtime, "FaceCodeInjector.exe"), "--tags-off")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private bool CanRead()
    {
        if (!CanStart()) return false;
        if (_injected) return true;
        SetStatus("Önce INJECT düğmesine basıp yüz okuyucusunu yükleyin.", true);
        return false;
    }

    private async void FindClick(object sender, RoutedEventArgs e)
    {
        if (!CanRead()) return;
        _busy = true;
        SetBusyButtons();
        NearbyList.Items.Clear();
        NearbyList.Items.Add(new ListBoxItem { Content = "Oyuncular taranıyor...", IsEnabled = false });
        SetStatus("250 metre içindeki oyuncular taranıyor...", false);
        try
        {
            var runtime = await Task.Run(PrepareRuntime);
            string nearbyPath;
            Hide();
            try
            {
                FocusFiveM();
                await Task.Delay(300);
                nearbyPath = await RunReaderAsync(runtime, "--nearby");
            }
            finally { Show(); Activate(); }

            var players = await ReadNearbyAsync(nearbyPath);
            NearbyList.Items.Clear();
            foreach (var player in players)
            {
                var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 2, 4, 2) };
                var headerRow = new Grid();
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new TextBlock
                {
                    Text = player.DisplayName,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("Ink"),
                    FontSize = 12
                };
                var idBadge = new Border
                {
                    Background = (Brush)FindResource("AccentSubtle"),
                    BorderBrush = (Brush)FindResource("AccentBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1)
                };
                idBadge.Child = new TextBlock
                {
                    Text = $"ID {player.ServerId}",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("Accent")
                };

                Grid.SetColumn(nameBlock, 0);
                Grid.SetColumn(idBadge, 1);
                headerRow.Children.Add(nameBlock);
                headerRow.Children.Add(idBadge);

                var subRow = new TextBlock
                {
                    Text = $"Mesafe: {player.DistanceMeters:0.0}m  ·  Cinsiyet: {player.Sex}",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("Muted"),
                    Margin = new Thickness(0, 3, 0, 0)
                };

                panel.Children.Add(headerRow);
                panel.Children.Add(subRow);

                NearbyList.Items.Add(new ListBoxItem { Tag = player.ServerId, Content = panel });
            }

            if (players.Count > 0)
            {
                NearbyList.SelectedIndex = 0;
                SetStatus($"{players.Count} oyuncu bulundu. Seçilen ID alana aktarıldı.", false);
            }
            else
            {
                NearbyList.Items.Add(new ListBoxItem { Content = "250 metre içinde başka oyuncu bulunamadı.", IsEnabled = false });
                SetStatus("250 metre içinde başka oyuncu yok. ID boşken kendi yüzünüzü çıkarabilirsiniz.", false);
            }
        }
        catch (Exception ex)
        {
            NearbyList.Items.Clear();
            SetStatus(ex.Message, true);
        }
        finally
        {
            _busy = false;
            UpdateReaderState();
        }
    }

    private void NearbySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NearbyList.SelectedItem is ListBoxItem { Tag: int id })
            TargetIdBox.Text = id.ToString();
    }

    private async void CaptureClick(object sender, RoutedEventArgs e)
    {
        if (!CanRead()) return;
        var input = TargetIdBox.Text.Trim();
        int? serverId = null;
        if (input.Length != 0)
        {
            if (!int.TryParse(input, out var parsed) || parsed is < 1 or > 65535)
            {
                SetStatus("Sunucu ID 1–65535 arasında geçerli bir sayı olmalıdır.", true);
                return;
            }
            serverId = parsed;
        }

        _busy = true;
        SetBusyButtons();
        CaptureButton.Content = "OKUNUYOR...";
        SetStatus(serverId is null
            ? "Kendi karakterinizin yüz verisi okunuyor..."
            : $"FiveM ID {serverId} için yüz verisi okunuyor...", false);

        try
        {
            var runtime = await Task.Run(PrepareRuntime);
            string rawPath;
            Hide();
            try
            {
                FocusFiveM();
                await Task.Delay(300);
                rawPath = await RunReaderAsync(runtime, serverId?.ToString());
            }
            finally { Show(); Activate(); }

            var raw = await File.ReadAllTextAsync(rawPath);
            using (var json = JsonDocument.Parse(raw))
            {
                var root = json.RootElement;
                if (root.GetProperty("schema").GetString() != "velthyra.facecode.v1")
                    throw new InvalidOperationException("Yüz verisi geçersiz.");
                if (serverId is int target &&
                    (root.GetProperty("targetServerId").GetInt32() != target ||
                     root.GetProperty("distanceMeters").GetSingle() > 250f))
                    throw new InvalidOperationException("Hedef oyuncu veya mesafe doğrulanamadı.");
            }

            var code = RinaFaceCode.Convert(raw);
            var xml = MenyooFaceXml.Convert(raw);
            var codes = Path.Combine(_home, "Codes");
            Directory.CreateDirectory(codes);
            var label = serverId?.ToString() ?? "Kendi";
            var basePath = Path.Combine(codes, $"Face-{label}-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
            var path = basePath + ".json";
            await File.WriteAllTextAsync(path, code, new UTF8Encoding(false));
            await File.WriteAllTextAsync(basePath + "-Menyoo.xml", xml, new UTF8Encoding(false));
            _lastCode = code;
            _lastXml = xml;
            _lastFile = path;
            RefreshCodePreview();

            using (var parsed = JsonDocument.Parse(code))
                SexText.Text = "KARAKTER: " + (parsed.RootElement.GetProperty("Sex").GetInt32() == 2 ? "KADIN" : "ERKEK");

            CodeBox.Foreground = (Brush)FindResource("Ink");
            CopyButton.IsEnabled = FolderButton.IsEnabled = true;

            try
            {
                Clipboard.SetText(code);
                SetStatus("JSON ve Menyoo XML kaydedildi; JSON panoya kopyalandı.", false);
            }
            catch (ExternalException)
            {
                SetStatus("Yüz kodu kaydedildi. KODU KOPYALA butonunu kullanabilirsiniz.", false);
            }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally
        {
            _busy = false;
            UpdateReaderState();
            CaptureButton.Content = "YÜZ KODUNU ÇIKAR";
        }
    }

    private async void CopyClick(object sender, RoutedEventArgs e)
    {
        var content = _showXml ? _lastXml : _lastCode;
        if (content is null) return;
        try
        {
            Clipboard.SetText(content);
            CopyButton.Content = "KOPYALANDI";
            SetStatus((_showXml ? "Menyoo XML" : "JSON kodu") + " panoya kopyalandı.", false);
            await Task.Delay(1500);
            CopyButton.Content = "KODU KOPYALA";
        }
        catch (ExternalException)
        {
            SetStatus("Pano şu anda kullanılamıyor.", true);
        }
    }

    private void JsonFormatClick(object sender, RoutedEventArgs e)
    {
        _showXml = false;
        RefreshCodePreview();
    }

    private void XmlFormatClick(object sender, RoutedEventArgs e)
    {
        _showXml = true;
        RefreshCodePreview();
    }

    private void RefreshCodePreview()
    {
        CodeBox.Text = (_showXml ? _lastXml : _lastCode) ??
            "Yüz kodu oluşturulduğunda burada görünecektir.";
        JsonFormatButton.Style = (Style)FindResource(_showXml ? "SecondaryButton" : "PrimaryButton");
        XmlFormatButton.Style = (Style)FindResource(_showXml ? "PrimaryButton" : "SecondaryButton");
    }

    private void FolderClick(object sender, RoutedEventArgs e)
    {
        if (_lastFile is null) return;
        Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_lastFile)!) { UseShellExecute = true });
    }

    private bool CanStart()
    {
        if (_busy) return false;
        if (!GameIsRunning())
        {
            SetStatus("FiveM bulunamadı. Lütfen önce oyuna giriş yapın.", true);
            return false;
        }
        return true;
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(error ? "Danger" : "Muted");
        ActionStatus.Text = message;
        ActionStatus.Foreground = (Brush)FindResource(error ? "Danger" : "Muted");
    }

    private void UpdateGameStatus()
    {
        var running = GameIsRunning();
        GameDot.Fill = (Brush)FindResource(running ? "Success" : "Muted");
        if (!running && _injected && !_busy)
        {
            _injected = false;
            CloseTagOverlay();
            UpdateReaderState();
        }
        if (!_busy && WorkbenchPage.Visibility == Visibility.Visible)
        {
            StatusText.Text = running ? "FiveM açık · Hazır" : "FiveM bekleniyor...";
            StatusText.Foreground = (Brush)FindResource(running ? "Success" : "Muted");
        }
    }

    private static bool GameIsRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase) &&
                        process.ProcessName.EndsWith("_GTAProcess", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return false;
    }

    private static void FocusFiveM()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase) &&
                        process.ProcessName.EndsWith("_GTAProcess", StringComparison.OrdinalIgnoreCase) &&
                        process.MainWindowHandle != IntPtr.Zero)
                    {
                        SetForegroundWindow(process.MainWindowHandle);
                        return;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        throw new InvalidOperationException("FiveM penceresi bulunamadı.");
    }

    private string PrepareRuntime()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var dll = ReadResource(assembly, "FaceSt.FaceStReader.dll");
        var injector = ReadResource(assembly, "FaceSt.FaceCodeInjector.exe");
        var version = Convert.ToHexString(SHA256.HashData(dll.Concat(injector).ToArray()))[..12];
        var runtime = Path.Combine(_home, "Runtime", version);
        Directory.CreateDirectory(runtime);
        WriteVerified(Path.Combine(runtime, "FaceStReader.dll"), dll);
        WriteVerified(Path.Combine(runtime, "FaceCodeInjector.exe"), injector);
        return runtime;
    }

    private static byte[] ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Face Code bileşeni bulunamadı.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void WriteVerified(string path, byte[] bytes)
    {
        if (File.Exists(path) && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(bytes))) return;
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }

    private static async Task<string> RunCommandAsync(string runtime, string? argument)
    {
        var psi = new ProcessStartInfo(Path.Combine(runtime, "FaceCodeInjector.exe"))
        {
            WorkingDirectory = runtime,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (argument is not null) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Yüz okuyucu başlatılamadı.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw new TimeoutException("Yüz okuma zaman aşımına uğradı.");
        }
        var stdout = (await output).Trim();
        var stderr = (await errors).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Yüz okunamadı." : stderr);
        return stdout;
    }

    private static async Task<string> RunReaderAsync(string runtime, string? argument)
    {
        var path = Path.GetFullPath(await RunCommandAsync(runtime, argument));
        var folder = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FaceSt", "Captures")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidOperationException("Yüz verisi dosyası bulunamadı.");
        return path;
    }

    private static async Task<List<NearbyPlayer>> ReadNearbyAsync(string path)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = json.RootElement;
        if (root.GetProperty("schema").GetString() != "velthyra.nearby.v1")
            throw new InvalidOperationException("Oyuncu listesi geçersiz.");
        var entries = root.GetProperty("players");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 256)
            throw new InvalidOperationException("Oyuncu listesi geçersiz.");
        var players = new List<NearbyPlayer>();
        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.GetProperty("serverId").GetInt32();
            var distance = entry.GetProperty("distanceMeters").GetSingle();
            if (id is < 1 or > 65535 || !float.IsFinite(distance) || distance < 0 || distance > 250 ||
                players.Any(player => player.ServerId == id))
                throw new InvalidOperationException("Oyuncu listesi geçersiz.");
            var name = entry.TryGetProperty("displayName", out var displayName) &&
                       displayName.ValueKind == JsonValueKind.String ? displayName.GetString() : null;
            name = string.IsNullOrWhiteSpace(name) ? "Oyuncu" :
                new string(name.Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
            var model = entry.TryGetProperty("modelHash", out var modelValue) &&
                        modelValue.TryGetUInt32(out var parsedModel) ? parsedModel : 0;
            var sex = model switch
            {
                0x705E61F2 => "ERKEK",
                0x9C9EFFD8 => "KADIN",
                _ => "BİLİNMİYOR"
            };
            players.Add(new NearbyPlayer(id, name.Length == 0 ? "Oyuncu" : name, distance, sex));
        }
        return players;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
