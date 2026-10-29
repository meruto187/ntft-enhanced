using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NtftEnhanced.Ipc;
using Forms = System.Windows.Forms;

namespace NtftEnhanced.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Forms.NotifyIcon _icon;
    private long _seenBlocks;
    private bool _proxyApplied;
    private bool _autoOn = true;
    private bool _locked;
    private bool _enabled;

    public MainWindow()
    {
        InitializeComponent();
        _icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = Product.Name
        };
        _icon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
        };
        Closed += (_, _) => _icon.Dispose();
    }

    private async Task RefreshAsync()
    {
        var response = await SendAsync("""{"op":"status"}""");
        if (response is null)
        {
            _enabled = false;
            PaintPower(false);
            StateText.Text = "Servis kapalı";
            LockText.Text = "";
            MetaText.Text = "Pencere servise ulaşamıyor.";
            return;
        }

        var status = response.Value.GetProperty("status");
        var enabled = status.GetProperty("protectionEnabled").GetBoolean();
        var locked = status.GetProperty("locked").GetBoolean();
        _enabled = enabled;
        _locked = locked;
        var count = status.GetProperty("domainCount").GetInt32();
        var auto = status.GetProperty("autoUpdate").GetBoolean();
        _autoOn = auto;
        var lastNode = status.GetProperty("lastListUpdateUtc");
        var lastText = lastNode.ValueKind == JsonValueKind.Null
            ? "henüz yok"
            : lastNode.GetDateTimeOffset().ToLocalTime().ToString("g");
        var blocks = status.GetProperty("blockCount").GetInt64();
        var pac = status.GetProperty("pacPath").GetString() ?? "";
        PaintPower(enabled);
        StateText.Text = enabled ? "Koruma açık" : "Kapalı";
        LockText.Text = locked ? "Kilitli" : enabled ? "Kilit yok" : "";
        LockMenu.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        AutoMenu.Header = auto ? "Kendiliğinden güncelleme: açık" : "Kendiliğinden güncelleme: kapalı";
        MetaText.Text = $"{count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} alan adı. Son liste: {lastText}.";
        if (blocks > _seenBlocks && _seenBlocks > 0)
        {
            var when = DateTime.Now.ToString("HH:mm");
            BlockText.Text = UiText.BlockedNotification + " — " + when;
            _icon.ShowBalloonTip(4000, Product.Name, UiText.BlockedNotification + "\n" + when, Forms.ToolTipIcon.Info);
        }
        else if (blocks > 0 && string.IsNullOrEmpty(BlockText.Text))
        {
            BlockText.Text = UiText.BlockedNotification;
        }
        _seenBlocks = blocks;

        if (enabled && File.Exists(pac))
        {
            UserProxy.Apply(pac);
            _proxyApplied = true;
        }
        else if (_proxyApplied)
        {
            UserProxy.ClearIfOurs();
            _proxyApplied = false;
        }
    }

    private async void Power_Click(object sender, RoutedEventArgs e)
    {
        if (!_enabled)
        {
            await ShowResultAsync(await SendAsync("""{"op":"enable"}"""));
            return;
        }

        var secret = "";
        if (_locked)
        {
            var entered = AskSecret("Korumayı kapatmak için parola.");
            if (entered is null)
                return;
            secret = entered;
        }

        var payload = JsonSerializer.Serialize(new { op = "disable", secret });
        await ShowResultAsync(await SendAsync(payload));
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsButton.ContextMenu is null)
            return;
        SettingsButton.ContextMenu.PlacementTarget = SettingsButton;
        SettingsButton.ContextMenu.IsOpen = true;
    }

    private void PaintPower(bool on)
    {
        PowerButton.ApplyTemplate();
        if (PowerButton.Template.FindName("Circle", PowerButton) is not System.Windows.Controls.Border circle)
            return;
        if (PowerButton.Template.FindName("Glyph", PowerButton) is not System.Windows.Shapes.Path glyph)
            return;
        circle.Background = new System.Windows.Media.SolidColorBrush(on
            ? System.Windows.Media.Color.FromRgb(0x1C, 0x19, 0x17)
            : System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xF9));
        circle.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD6, 0xD3, 0xD1));
        circle.BorderThickness = on ? new Thickness(0) : new Thickness(1);
        glyph.Stroke = new System.Windows.Media.SolidColorBrush(on
            ? System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xF9)
            : System.Windows.Media.Color.FromRgb(0x1C, 0x19, 0x17));
    }

    private async void Update_Click(object sender, RoutedEventArgs e) =>
        await ShowResultAsync(await SendAsync("""{"op":"update"}"""));

    private async void Auto_Click(object sender, RoutedEventArgs e)
    {
        var next = !_autoOn;
        string secret = "";
        if (_locked)
        {
            var entered = AskSecret(next
                ? "Kendiliğinden güncellemeyi açmak için parola."
                : "Kendiliğinden güncellemeyi kapatmak için parola.");
            if (entered is null)
                return;
            secret = entered;
        }
        var payload = JsonSerializer.Serialize(new { op = "autoupdate", secret, enabled = next });
        await ShowResultAsync(await SendAsync(payload));
    }

    private async void Lock_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LockDialog();
        if (dialog.ShowDialog() != true)
            return;
        var payload = JsonSerializer.Serialize(new
        {
            op = "lock",
            password = dialog.Password,
            confirm = dialog.Confirm
        });
        var response = await SendAsync(payload);
        if (response is null)
            return;
        if (!response.Value.GetProperty("ok").GetBoolean())
        {
            MessageBox.Show(response.Value.GetProperty("message").GetString(), Product.Name);
            return;
        }
        var key = response.Value.GetProperty("recoveryKey").GetString() ?? "";
        var harden = JsonSerializer.Serialize(new
        {
            op = "harden",
            secret = dialog.Password,
            doh = dialog.Doh,
            safeSearch = dialog.SafeSearch,
            familyDns = dialog.Family,
            blockDohIps = dialog.BlockIps
        });
        await SendAsync(harden);
        MessageBox.Show("Kurtarma anahtarı bir kez gösterilir. Arkadaşın bunu makine dışında saklar.\n\n" + key, Product.Name);
        await RefreshAsync();
    }

    private async void Harden_Click(object sender, RoutedEventArgs e)
    {
        var secret = AskSecret("Sertleştirme için parola. Kilit yoksa boş kalabilir.");
        if (secret is null)
            return;
        var dialog = new LockDialog(hardeningOnly: true);
        if (dialog.ShowDialog() != true)
            return;
        var payload = JsonSerializer.Serialize(new
        {
            op = "harden",
            secret,
            doh = dialog.Doh,
            safeSearch = dialog.SafeSearch,
            familyDns = dialog.Family,
            blockDohIps = dialog.BlockIps
        });
        await ShowResultAsync(await SendAsync(payload));
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var secret = AskSecret("Kaldırmak için parola. Vekil kapanır. Eski bir hosts bloğu varsa silinmeye çalışılır.");
        if (secret is null)
            return;
        var payload = JsonSerializer.Serialize(new { op = "uninstall", secret });
        var response = await SendAsync(payload);
        await ShowResultAsync(response);
        if (response?.GetProperty("ok").GetBoolean() == true)
            StartSetup("uninstall");
    }

    private void Install_Click(object sender, RoutedEventArgs e) => StartSetup("install");

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md");
        var text = File.Exists(path) ? File.ReadAllText(path) : "StevenBlack/hosts porn-only. MIT ve CC BY 4.0.";
        MessageBox.Show(text, Product.Name);
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (query.Length < 2)
        {
            Results.Items.Clear();
            Results.Visibility = Visibility.Collapsed;
            return;
        }
        var payload = JsonSerializer.Serialize(new { op = "search", q = query });
        var response = await SendAsync(payload);
        Results.Items.Clear();
        if (response is null || !response.Value.TryGetProperty("domains", out var domains))
        {
            Results.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var domain in domains.EnumerateArray())
            Results.Items.Add(domain.GetString());
        Results.Visibility = Results.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task ShowResultAsync(JsonElement? response)
    {
        if (response is null)
        {
            MessageBox.Show("Servis durmuş, bu yüzden pencere ona ulaşamıyor. NTFT-Enhanced-Setup dosyasını yeniden çalıştırın.", Product.Name);
            return;
        }
        MessageBox.Show(response.Value.GetProperty("message").GetString(), Product.Name);
        await RefreshAsync();
    }

    private static async Task<JsonElement?> SendAsync(string line)
    {
        try
        {
            using var token = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var response = await PipeEndpoint.RequestAsync(Product.PipeName, line, token.Token);
            if (response is null)
                return null;
            return JsonSerializer.Deserialize<JsonElement>(response);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? AskSecret(string prompt)
    {
        var box = new PasswordBox { Margin = new Thickness(0, 8, 0, 0) };
        var window = new Window
        {
            Title = Product.Name,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children = { new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, box }
            }
        };
        var button = new Button { Content = "Tamam", Margin = new Thickness(0, 12, 0, 0), IsDefault = true };
        button.Click += (_, _) => window.DialogResult = true;
        ((StackPanel)window.Content).Children.Add(button);
        return window.ShowDialog() == true ? box.Password : null;
    }

    private static void StartSetup(string argument)
    {
        var setup = Path.Combine(AppContext.BaseDirectory, "NTFT-Enhanced-Setup.exe");
        if (!File.Exists(setup))
        {
            MessageBox.Show("Kurulum programı bu klasörde yok.", Product.Name);
            return;
        }
        Process.Start(new ProcessStartInfo(setup, argument) { UseShellExecute = true, Verb = "runas" });
    }
}

internal static class UserProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public static void Apply(string pacPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("AutoConfigURL", "file:///" + pacPath.Replace('\\', '/'));
    }

    public static void ClearIfOurs()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        var current = key.GetValue("AutoConfigURL") as string ?? "";
        if (current.Contains("NTFT-Enhanced", StringComparison.OrdinalIgnoreCase))
            key.DeleteValue("AutoConfigURL", false);
    }
}

internal sealed class LockDialog : Window
{
    private readonly PasswordBox _password = new();
    private readonly PasswordBox _confirm = new();
    private readonly System.Windows.Controls.CheckBox _doh = new() { Content = "Tarayıcı DNS-over-HTTPS kapatılsın" };
    private readonly System.Windows.Controls.CheckBox _safe = new() { Content = "Google ve Bing SafeSearch açılsın" };
    private readonly System.Windows.Controls.CheckBox _family = new() { Content = "Cloudflare Family DNS kullanılsın" };
    private readonly System.Windows.Controls.CheckBox _ips = new() { Content = "Bilinen DoH adreslerine 443 engeli" };

    public LockDialog(bool hardeningOnly = false)
    {
        Title = Product.Name;
        Width = 520;
        Height = hardeningOnly ? 320 : 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        if (!hardeningOnly)
        {
            panel.Children.Add(new TextBlock { Text = "Arkadaş parolayı iki kez yazar. YouTube kısıtı yok.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(_password);
            panel.Children.Add(_confirm);
        }
        panel.Children.Add(new TextBlock { Text = "İstenen kutular işaretlenir. İşaretsiz de devam edilir.", Margin = new Thickness(0, 12, 0, 8), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_doh);
        panel.Children.Add(_safe);
        panel.Children.Add(_family);
        panel.Children.Add(_ips);
        var button = new Button { Content = "Kaydet", Margin = new Thickness(0, 16, 0, 0), IsDefault = true };
        button.Click += (_, _) => DialogResult = true;
        panel.Children.Add(button);
        Content = panel;
    }

    public string Password => _password.Password;
    public string Confirm => _confirm.Password;
    public bool Doh => _doh.IsChecked == true;
    public bool SafeSearch => _safe.IsChecked == true;
    public bool Family => _family.IsChecked == true;
    public bool BlockIps => _ips.IsChecked == true;
}
