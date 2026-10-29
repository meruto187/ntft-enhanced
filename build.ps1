$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "artifacts\publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

$projects = @(
    "src\NtftEnhanced.Service\NtftEnhanced.Service.csproj",
    "src\NtftEnhanced.App\NtftEnhanced.App.csproj",
    "src\NtftEnhanced.Setup\NtftEnhanced.Setup.csproj"
)
foreach ($project in $projects) {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true -o $out
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Copy-Item (Join-Path $root "KURTARMA.md") $out
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $out
Copy-Item (Join-Path $root "README.md") $out
Write-Host "Hazır: $out\NTFT-Enhanced-Setup.exe"
