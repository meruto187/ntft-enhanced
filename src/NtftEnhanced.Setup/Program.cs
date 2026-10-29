using System.Diagnostics;
using Microsoft.Win32;

var command = args.Length > 0 ? args[0] : "install";
var form = new Form
{
    Text = "NTFT-Enhanced",
    Width = 520,
    Height = 240,
    FormBorderStyle = FormBorderStyle.FixedDialog,
    MaximizeBox = false,
    StartPosition = FormStartPosition.CenterScreen
};
var label = new Label
{
    Dock = DockStyle.Fill,
    Padding = new Padding(16),
    Text = command == "uninstall" ? "Kaldırılıyor…" : "Kuruluyor…"
};
form.Controls.Add(label);
form.Shown += (_, _) =>
{
    label.Text = command == "uninstall" ? Remove() : Install();
};
System.Windows.Forms.Application.Run(form);

static string Install()
{
    var source = AppContext.BaseDirectory;
    var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NTFT-Enhanced");
    var service = NtftEnhanced.Product.ServiceName;
    Run("sc.exe", $"stop {service}");
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(source))
    {
        var name = Path.GetFileName(file);
        if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            continue;
        File.Copy(file, Path.Combine(destination, name), true);
    }

    var bin = Path.Combine(destination, "NtftEnhanced.Service.exe");
    Run("sc.exe", $"delete {service}");
    var created = Run("sc.exe", $"create {service} binPath= \"{bin}\" start= auto");
    if (created != 0)
        return "Servis kaydı yazılamadı. Kurulumu yeniden, yönetici olarak çalıştırın.";
    Run("sc.exe", $"description {service} \"NTFT-Enhanced pornografi filtresi\"");
    Run("sc.exe", $"failure {service} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
    var started = Run("sc.exe", $"start {service}");

    var ui = Path.Combine(destination, "NTFT-Enhanced.exe");
    using (var run = Registry.LocalMachine.CreateSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
        run.SetValue(NtftEnhanced.Product.Name, $"\"{ui}\"");
    CreateShortcut(ui, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    CreateShortcut(ui, Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));

    if (started != 0)
    {
        return "Dosyalar kopyalandı ama servis başlamadı. Windows Güvenliği imzasız NtftEnhanced.Service.dll dosyasını engelliyor. " +
               "Bildirimdeki ayrıntıdan bu dosyaya izin verin, sonra kurulumu yeniden çalıştırın. Boş bir konsol penceresi açılmaz.";
    }

    Process.Start(new ProcessStartInfo(ui) { UseShellExecute = true });
    return "Kuruldu. Açılan pencereden Korumayı açın. Bu pencereyi kapatabilirsiniz.";
}

static string Remove()
{
    var service = NtftEnhanced.Product.ServiceName;
    Run("sc.exe", $"stop {service}");
    Run("sc.exe", $"delete {service}");
    using (var run = Registry.LocalMachine.OpenSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true))
        run?.DeleteValue(NtftEnhanced.Product.Name, throwOnMissingValue: false);
    return "Servis kaldırıldı. Hosts yedeği C:\\ProgramData\\NTFT-Enhanced\\backups altında duruyor.";
}

static void CreateShortcut(string target, string directory)
{
    Directory.CreateDirectory(directory);
    var shortcut = Path.Combine(directory, "NTFT-Enhanced.lnk");
    var script = $"$s = New-Object -ComObject WScript.Shell; $l = $s.CreateShortcut('{shortcut.Replace("'", "''")}'); $l.TargetPath = '{target.Replace("'", "''")}'; $l.Save()";
    Run("powershell.exe", "-NoProfile -Command \"" + script.Replace("\"", "\\\"") + "\"");
}

static int Run(string file, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(file, arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    });
    if (process is null || !process.WaitForExit(20000))
        return 1;
    return process.ExitCode;
}
