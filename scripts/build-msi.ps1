$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\DigiNx.PrintBridge\DigiNx.PrintBridge.csproj'
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'
$payload = Join-Path $root 'installer\payload'
$msi = Join-Path $dist 'DigiNx-Print-Bridge-Setup-x64.msi'

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publish | Out-Null
New-Item -ItemType Directory -Force -Path $payload | Out-Null

dotnet restore $project
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish `
  /p:PublishSingleFile=true /p:PublishTrimmed=false /p:IncludeNativeLibrariesForSelfExtract=true

$exe = Join-Path $publish 'DigiNx.PrintBridge.exe'
if (-not (Test-Path $exe)) { throw "Published bridge executable not found: $exe" }
Copy-Item $exe (Join-Path $payload 'DigiNx.PrintBridge.exe') -Force

$installer = Join-Path $root 'installer'
$wxs = Join-Path $installer 'DigiNx.PrintBridge.wxs'
wix build $wxs -arch x64 -bindpath $installer -o $msi
if (-not (Test-Path $msi)) { throw "MSI was not created: $msi" }

$hash = (Get-FileHash $msi -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  DigiNx-Print-Bridge-Setup-x64.msi" | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Built: $msi" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor Green
