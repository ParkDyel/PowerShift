param([switch]$EnableStartup, [switch]$ScriptHost)
$ErrorActionPreference = 'Stop'
$hostPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$scriptPath = Join-Path $PSScriptRoot 'PowerShift.ps1'
$exePath = Join-Path $PSScriptRoot 'bin\PowerShift.exe'
if (!$ScriptHost -and !(Test-Path -LiteralPath $exePath)) {
    & (Join-Path $PSScriptRoot 'Build.ps1') | Out-Null
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $PSScriptRoot 'PowerShift.lnk'))
if ($ScriptHost) {
    $shortcut.TargetPath = $hostPath
    $shortcut.Arguments = '-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "' + $scriptPath + '" -Settings'
} else {
    $shortcut.TargetPath = $exePath
    $shortcut.Arguments = '--settings'
}
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.WindowStyle = 1
$shortcut.Description = 'PowerShift - Windows power plan switcher'
$shortcut.IconLocation = (Join-Path $env:WINDIR 'System32\powercpl.dll') + ',0'
$shortcut.Save()
if ($EnableStartup) {
    if ($ScriptHost) {
        $registration = Start-Process -FilePath $hostPath -ArgumentList @('-NoProfile','-STA','-WindowStyle','Hidden','-ExecutionPolicy','RemoteSigned','-File',('"' + $scriptPath + '"'),'-EnableStartup') -WindowStyle Hidden -PassThru -Wait
    } else {
        $registration = Start-Process -FilePath $exePath -ArgumentList '--enable-startup' -WindowStyle Hidden -PassThru -Wait
    }
    if ($registration.ExitCode -ne 0) { throw 'Startup registration failed.' }
}
Write-Output 'Created PowerShift.lnk. Launch it to open PowerShift.'
