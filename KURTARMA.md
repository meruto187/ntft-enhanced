# Kurtarma

Bu adımlar yönetici yetkisi ister. Güncel sürüm listeyi hosts dosyasına yazmaz. Eski bir sürüm yazdıysa ve internet kilitlendiyse hosts yedeğini geri koyun.

## 1. Servisi durdur

Yönetici PowerShell:

```powershell
sc.exe stop NtftEnhanced
```

## 2. Hosts dosyasını geri al

Yedekler:

`C:\ProgramData\NTFT-Enhanced\backups`

- `hosts-original` ilk kurulumdaki dosyadır.
- `hosts-pre-...` her yazımdan önceki kopyadır. En yeni dosyayı kullanın.

Geri yükleme:

```powershell
Copy-Item "$env:ProgramData\NTFT-Enhanced\backups\hosts-original" "$env:SystemRoot\System32\drivers\etc\hosts" -Force
ipconfig /flushdns
```

Dosyada şu iki satır durmalıdır:

```
127.0.0.1 localhost
::1 localhost
```

`BEGIN NTFT-ENHANCED BLOCK` ile `END NTFT-ENHANCED BLOCK` arasındaki satırlar silinebilir. Bu işaretlerin dışındaki satırlar sizin eski kayıtlarınızdır.

## 3. Vekili kapat

`C:\ProgramData\NTFT-Enhanced\proxy.pac` dosyasını silin.

Windows ayarlarında vekil betiği NTFT-Enhanced yolunu gösteriyorsa o adresi temizleyin:

Ayarlar, Ağ ve internet, Vekil sunucu, Kurulum betiği.

## 4. Sertleştirmeyi geri al

Bunlar yalnızca pencereden açıldıysa vardır.

- `HKLM\SOFTWARE\Policies\Google\Chrome`
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`
- `HKLM\SOFTWARE\Policies\BraveSoftware\Brave`
- `HKLM\SOFTWARE\Policies\Chromium`
- `HKLM\SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS`

NTFT-Enhanced’ın yazdığı `DnsOverHttpsMode`, `BuiltInDnsClientEnabled`, `ForceGoogleSafeSearch`, `ForceBingSafeSearch`, `QuicAllowed`, `ProxySettings` değerlerini, `C:\ProgramData\NTFT-Enhanced\hardening.json` içindeki önceki değerlere döndürün. Dosyada `kind` alanı `missing` ise o değeri silin.

Cloudflare Family açıldıysa DNS sunucularını hardening.json içindeki `previousDns` listesine döndürün. Bilinmiyorsa yönlendiricinizin DNS’ine veya `1.1.1.1` adresine alabilirsiniz. YouTube kısıtı yazılmaz.

Güvenlik duvarında adı `NTFT-Enhanced DoH` ile başlayan kuralları silin.

## 5. Programı kaldır

```powershell
sc.exe delete NtftEnhanced
Remove-Item "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NTFT-Enhanced" -ErrorAction SilentlyContinue
```

`C:\Program Files\NTFT-Enhanced` klasörünü silebilirsiniz. Yedek klasörünü internet düzelene kadar silmeyin.
