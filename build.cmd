@echo off
rem DiskGuard v9.0 build script (Native DirectX WPF x64 - ZERO WebView2 / ZERO Chromium!)
setlocal
cd /d "%~dp0"
if not exist bin mkdir bin
set ICON_ARG=
if exist assets\DiskGuard.ico set ICON_ARG=/win32icon:assets\DiskGuard.ico
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 %ICON_ARG% /out:bin\DiskGuard.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Xaml.dll "/r:%WPF%\WindowsBase.dll" "/r:%WPF%\PresentationCore.dll" "/r:%WPF%\PresentationFramework.dll" "/r:%WPF%\WindowsFormsIntegration.dll" src\Program.cs src\SelfTest.cs src\Core\Config.cs src\Core\Win32Fs.cs src\Core\DiskMonitor.cs src\Core\CleanEngine.cs src\Core\SpaceAnalyzer.cs src\Core\DockerManager.cs src\Core\SystemHealth.cs src\Core\Updater.cs src\Core\WslTuner.cs src\Core\ExtraScanners.cs src\Core\UserFolderAudit.cs src\Core\SafeRelocator.cs src\Core\SoftwareManager.cs src\Core\NetworkEnvManager.cs src\UI\Theme.cs src\UI\TrayApp.cs src\Wpf\WpfTheme.cs src\Wpf\MainWindow.cs src\Wpf\PageSmartHome.cs src\Wpf\PagesSpace.cs src\Wpf\PagesAppsDocker.cs src\Wpf\PagesSysSettings.cs
exit /b %errorlevel%
