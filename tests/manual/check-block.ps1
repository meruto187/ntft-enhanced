# Gerçek hosts dosyasını değiştirmez. Kurulu bir NTFT-Enhanced için okuma kontrolü yapar.
$hostsPath = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
if (-not (Test-Path $hostsPath)) { throw "hosts dosyası yok." }
$text = Get-Content $hostsPath -Raw
$begin = "# BEGIN NTFT-ENHANCED BLOCK"
$end = "# END NTFT-ENHANCED BLOCK"
if ($text -notlike "*127.0.0.1 localhost*") { throw "localhost satırı yok." }
if ($text -notlike "*$begin*") {
    Write-Host "Blok bölümü yok. Koruma henüz açılmamış olabilir."
    exit 0
}
$start = $text.IndexOf($begin)
$finish = $text.IndexOf($end)
if ($finish -lt $start) { throw "İşaretler ters." }
$block = $text.Substring($start, $finish - $start)
$domain = ([regex]::Match($block, "(?m)^0\.0\.0\.0\s+(\S+)")).Groups[1].Value
if (-not $domain) { throw "Blokta alan adı yok." }
$resolved = Resolve-DnsName $domain -ErrorAction SilentlyContinue
$addresses = @($resolved | Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress)
if ($addresses -notcontains "0.0.0.0") { throw "$domain 0.0.0.0 çözülmedi." }
$microsoft = Resolve-DnsName microsoft.com -Type A -ErrorAction SilentlyContinue
$microsoftAddresses = @($microsoft | Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress)
if ($microsoftAddresses -contains "0.0.0.0") { throw "microsoft.com engelli. Hosts geri alınmalı." }
Write-Host "Blok var. $domain -> 0.0.0.0. microsoft.com engelli değil."
