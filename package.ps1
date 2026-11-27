# DiskGuard v9.0 Release Packager (Native DirectX WPF - ZERO WebView2 / Single-Process / macOS Style)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$ver = "9.0.1"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
$ico = Join-Path $root "assets\DiskGuard.ico"
$rel = Join-Path $root "release"
$stage = Join-Path $rel "stage"
$payloadZip = Join-Path $rel "payload.zip"
$portableZip = Join-Path $rel "DiskGuard-9.0.1-Portable-win-x64.zip"
$setupExe = Join-Path $rel "DiskGuard-Setup-9.0.1.exe"
$manifestPath = Join-Path $rel "latest.json"

Write-Output "[1/6] Generating multi-size DiskGuard.ico..."
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\make_icon.ps1") -OutPath $ico
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed" }

Write-Output "[2/6] Rebuilding bin\DiskGuard.exe (v$ver Native DirectX WPF, Zero WebView2)..."
if (-not (Test-Path "bin")) { New-Item -ItemType Directory -Force -Path "bin" | Out-Null }
if (Test-Path (Join-Path $root "src\App.config")) { Copy-Item (Join-Path $root "src\App.config") (Join-Path $root "bin\DiskGuard.exe.config") -Force }
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$ico" /out:bin\DiskGuard.exe `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Xaml.dll `
  "/r:$wpf\WindowsBase.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" "/r:$wpf\WindowsFormsIntegration.dll" `
  src\Program.cs src\SelfTest.cs src\Core\Config.cs src\Core\Win32Fs.cs src\Core\DiskMonitor.cs `
  src\Core\CleanEngine.cs src\Core\SpaceAnalyzer.cs src\Core\DockerManager.cs src\Core\SystemHealth.cs `
  src\Core\Updater.cs src\Core\WslTuner.cs src\Core\ExtraScanners.cs `
  src\Core\UserFolderAudit.cs src\Core\SafeRelocator.cs src\Core\SoftwareManager.cs src\Core\NetworkEnvManager.cs `
  src\UI\Theme.cs src\UI\TrayApp.cs `
  src\Wpf\WpfTheme.cs src\Wpf\MainWindow.cs src\Wpf\PageSmartHome.cs src\Wpf\PagesSpace.cs src\Wpf\PagesAppsDocker.cs src\Wpf\PagesSysSettings.cs
if ($LASTEXITCODE -ne 0) { throw "Main exe compile failed" }

Write-Output "[3/6] Staging ultra-light native release files (ZERO Chromium/WebView2 DLLs)..."
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

foreach ($f in @("DiskGuard.exe","DiskGuard.exe.config")) {
    $srcF = Join-Path $root ("bin\" + $f)
    if (Test-Path $srcF) { Copy-Item $srcF $stage -Force }
}

$readme = @(
    "DiskGuard v$ver - 原生 DirectX WPF 极速版（零 WebView2 / 单进程 / macOS 视觉工作台）",
    "",
    "核心亮点：",
    "1. 彻底移除 WebView2 与 6 个 Chromium 进程，窗口静置内存直降 ~90%，CPU 趋近 0%。",
    "2. 全新 5 入口 macOS / iOS 18 极简设计：打开即享「一键全面体检 & 一键智能修复」。",
    "3. 完整内置：User 专清清单、软件默认缓存改位、零丢失大文件安全搬家（软链+一键迁回）、软件卸载与残留排查、Docker/WSL2、PowerShell 7 升级、DNS 测速与代理修复、环境变量整理与快照回滚、默认自动更新。"
) -join "`r`n"
[System.IO.File]::WriteAllText((Join-Path $stage "使用说明.txt"), $readme, (New-Object System.Text.UTF8Encoding($false)))

if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $payloadZip -CompressionLevel Optimal
Copy-Item $payloadZip $portableZip -Force

Write-Output "[4/6] Compiling single-file GUI setup DiskGuard-Setup-9.0.1.exe..."
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$ico" `
  "/resource:$payloadZip,DiskGuard.payload.zip" "/out:$setupExe" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll `
  src\Setup\Installer.cs
if ($LASTEXITCODE -ne 0) { throw "Setup exe compile failed" }

Write-Output "[5/6] Generating auto-update manifest release\latest.json..."
$sha = (Get-FileHash $setupExe -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = @{
    version = $ver
    file = "DiskGuard-Setup-9.0.1.exe"
    sha256 = $sha
    notes = "v9.0.1: 修复 Docker Desktop 自定义路径/非C盘无法检测问题（6级自动探测+WSL Lxss注册表识别）；优化自动更新404中文提示；单进程仅 ~34 MB 内存。"
} | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

Write-Output "[6/6] Cleaning temp staging..."
Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue

Write-Output "PACKAGING_OK"
