param(
    [Parameter(Mandatory)][string]$IntDir,
    [Parameter(Mandatory)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
$rcFile = Join-Path $PSScriptRoot 'MenuStrings.rc'
$resFile = Join-Path $IntDir 'MenuStrings.res'
$dllFile = Join-Path $OutDir '7ZPM.Strings.dll'

# ── Find rc.exe from Windows 10 SDK ──

$sdkBin = 'C:\Program Files (x86)\Windows Kits\10\bin'
$rcExe = Get-ChildItem $sdkBin -Recurse -Filter 'rc.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like '*\x64' } |
    Sort-Object { [version]($_.DirectoryName -replace '.*\\(\d+\.\d+\.\d+\.\d+)\\.*','$1') } -Descending |
    Select-Object -First 1

if (-not $rcExe) {
    Write-Warning 'rc.exe not found — skipping MUI resource DLL build'
    exit 0
}

# ── Find link.exe from MSVC ──

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsDir = if (Test-Path $vswhere) { & $vswhere -latest -property installationPath 2>$null } else { $null }
$linkExe = if ($vsDir) {
    Get-ChildItem "$vsDir\VC\Tools\MSVC" -Recurse -Filter 'link.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like '*Hostx64\x64*' } |
        Sort-Object DirectoryName -Descending |
        Select-Object -First 1
}

if (-not $linkExe) {
    Write-Warning 'link.exe not found — skipping MUI resource DLL build'
    exit 0
}

# ── Compile ──

if (-not (Test-Path $IntDir)) { New-Item -ItemType Directory -Path $IntDir -Force | Out-Null }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

Write-Host "RC: $($rcExe.FullName)"
& $rcExe.FullName /nologo /fo $resFile $rcFile
if ($LASTEXITCODE -ne 0) { Write-Error "rc.exe failed ($LASTEXITCODE)"; exit 1 }

Write-Host "LINK: $($linkExe.FullName)"
& $linkExe.FullName /NOLOGO /DLL /NOENTRY /MACHINE:X64 /OUT:$dllFile $resFile
if ($LASTEXITCODE -ne 0) { Write-Error "link.exe failed ($LASTEXITCODE)"; exit 1 }

Write-Host "Built: $dllFile"
