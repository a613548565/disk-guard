# Pin DiskGuard tray icon to the taskbar and ask the running instance to show its window.
# ASCII only: Windows PowerShell 5.1 reads BOM-less files as ANSI.
$exe = 'E:\code\disk-guard\bin\DiskGuard.exe'
$found = $false
Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' | ForEach-Object {
    $p = Get-ItemProperty $_.PSPath
    if ($p.ExecutablePath -eq $exe) {
        Set-ItemProperty $_.PSPath -Name IsPromoted -Value 1 -Type DWord
        $found = $true
        Write-Output ("pinned: " + $_.PSChildName + " IsPromoted=" + (Get-ItemProperty $_.PSPath).IsPromoted)
    }
}
if (-not $found) { Write-Output 'NOT FOUND: tray icon not registered by Windows yet' }
Start-Process -FilePath $exe
Write-Output 'second instance launched'
