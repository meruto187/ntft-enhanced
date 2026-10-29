namespace NtftEnhanced.Hosts;

public sealed class HostsFileStore
{
    private readonly string _hostsPath;
    private readonly string _backupDirectory;

    public HostsFileStore(string hostsPath, string backupDirectory)
    {
        _hostsPath = hostsPath;
        _backupDirectory = backupDirectory;
    }

    internal Func<bool>? FailBeforeReplace { get; set; }

    public string Read() => File.Exists(_hostsPath) ? File.ReadAllText(_hostsPath) : "";

    public bool ContainsManagedBlock() => Read().Contains(Product.HostsBegin, StringComparison.Ordinal);

    public string? Apply(string newContent)
    {
        if (string.IsNullOrWhiteSpace(newContent))
            return "Yazılacak hosts içeriği boş.";
        if (!HostsMerger.PreservesLocalhost(Read(), newContent))
            return "localhost satırı yazımda kayboluyor.";

        Directory.CreateDirectory(_backupDirectory);
        var current = Read();
        var original = Path.Combine(_backupDirectory, "hosts-original");
        if (!File.Exists(original))
            File.WriteAllText(original, current);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var backup = Path.Combine(_backupDirectory, "hosts-pre-" + stamp);
        File.WriteAllText(backup, current);
        TrimBackups();

        var temp = Path.Combine(_backupDirectory, "hosts-new");
        try
        {
            File.WriteAllText(temp, newContent);
            if (File.ReadAllText(temp) != newContent)
                return "Geçici hosts dosyası doğrulanamadı.";
            if (FailBeforeReplace is not null && FailBeforeReplace())
                throw new IOException("Yazma yarıda kesildi.");

            var denied = Overwrite(_hostsPath, newContent);
            if (denied is not null)
            {
                Overwrite(_hostsPath, current);
                return denied;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Overwrite(_hostsPath, current);
            return "Hosts yazılamadı ve önceki kopya geri alındı.";
        }
    }

    private static string? Overwrite(string path, string content)
    {
        try
        {
            ClearReadOnly(path);
            var bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, bytes);
                return null;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            stream.SetLength(0);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.HResult == unchecked((int)0x80070005) ? BlockedBySecurity() : "Hosts dosyasına yazılamadı. Önceki içerik duruyor.";
        }
    }

    private static string BlockedBySecurity()
    {
        var kaspersky = Directory.Exists(@"C:\Program Files (x86)\Kaspersky Lab")
            || Directory.Exists(@"C:\Program Files\Kaspersky Lab");
        if (!kaspersky)
            return "Güvenlik yazılımı hosts dosyasını kilitliyor. NtftEnhanced.Service.exe için yazma iznini açın. Önceki içerik duruyor.";
        return "Kaspersky hosts dosyasını kilitliyor. Ayarlar, Güvenlik ayarları, Yetkisiz Erişim Önleme, Uygulamaları yönet. " +
               "NtftEnhanced.Service.exe kaydını açın. Dosya ve sistem kayıt defteri içinde İşletim sistemi, Sistem dosyaları, Kritik ayarlar, Hosts satırında Yaz sütununu İzin ver yapın. " +
               "İstisnalar bölümünde üst sürecin kısıtlamasını devralmayı kapatın. Sonra Korumayı aç'a tekrar basın.";
    }

    public string? RemoveManagedBlock()
    {
        if (!HostsMerger.TryExtractOutside(Read(), out var outside, out var error))
            return error;
        if (!Read().Contains(Product.HostsBegin, StringComparison.Ordinal))
            return null;
        return Apply(outside.TrimEnd() + "\r\n");
    }

    public bool RestoreLatestBackup()
    {
        if (!Directory.Exists(_backupDirectory))
            return false;
        var latest = Directory.GetFiles(_backupDirectory, "hosts-pre-*")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (latest is null)
            return false;
        ClearReadOnly(_hostsPath);
        File.Copy(latest, _hostsPath, true);
        return true;
    }

    private void TrimBackups()
    {
        var extras = Directory.GetFiles(_backupDirectory, "hosts-pre-*")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(10);
        foreach (var path in extras)
            File.Delete(path);
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path))
            return;
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
