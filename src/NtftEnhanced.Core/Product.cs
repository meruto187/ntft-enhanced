namespace NtftEnhanced;

public static class Product
{
    public const string Name = "NTFT-Enhanced";
    public const string ServiceName = "NtftEnhanced";
    public const string PipeName = "NtftEnhanced";
    public const string DataDirectoryName = "NTFT-Enhanced";
    public const string HostsBegin = "# BEGIN NTFT-ENHANCED BLOCK";
    public const string HostsEnd = "# END NTFT-ENHANCED BLOCK";
    public const string ListUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/porn-only/hosts";
    public const string ListApiUrl = "https://api.github.com/repos/StevenBlack/hosts/contents/alternates/porn-only/hosts";
    public const int MaxListBytes = 20 * 1024 * 1024;
    public const int ProductionMinimumDomains = 10_000;
    public const string FirewallPrefix = "NTFT-Enhanced DoH ";

    public static readonly string[] CanaryHosts =
    [
        "microsoft.com",
        "www.microsoft.com",
        "windowsupdate.microsoft.com",
        "update.microsoft.com",
        "login.microsoftonline.com"
    ];
}

public static class UiText
{
    public const string BlockedNotification = "Bir bağlantı engellendi";

    public const string Coverage =
        "Liste yerel vekilde durur. Hosts dosyasına on binlerce satır yazılmaz; Windows’un ad çözümlemesi şişmez. " +
        "Vekil kapanırsa bağlantı doğrudan gider, internet kesilmez. Reddit, X, Discord ve Telegram’ın kendisi listede yoksa açık kalır. " +
        "Sistem vekilini kullanmayan programlar listeyi aşar. WARP ve Proton sistem vekilini kullanmayabilir.";

    public const string DohWarning =
        "Tarayıcı DNS-over-HTTPS kullansa da vekil, CONNECT satırındaki alan adını görür. Sayfa şifresi çözülmez. " +
        "Helium, Chrome kayıt politikasını yok sayabilir. Helium elle denenir. Tutmazsa Firefox tabanlı tarayıcılar kullanılır. " +
        "Firefox ve LibreWolf sistem vekilini kendiliğinden açmaz.";

    public const string LockWarning =
        "Kilit yok. Arkadaşın parola belirlemeden koruma, uygulama içinden parola sorulmadan kapatılabilir.";

    public const string AdminBoundary =
        "Günlük hesap yönetici kalır. NTFT-Enhanced kendi kapatma yolunu parolaya bağlar. Yönetici, services.msc üzerinden servisi durdurabilir. " +
        "Görev yöneticisini kesmek veya çekirdek sürücüsü eklemek yok.";
}
