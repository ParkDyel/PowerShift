param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$InstallDirectory
)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$destination = [IO.Path]::GetFullPath($InstallDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Use a new, empty test installation directory.' }
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{58DE4696-91D0-4A18-A3F3-DCE538F13087}_is1'
$runKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'PowerShift.lnk'
$settingsPath = Join-Path $env:APPDATA 'PowerShift\settings.json'
function UninstallMetadata {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            $key = $root.OpenSubKey($uninstallKey)
            if ($key) { try { return @{ DisplayName = $key.GetValue('DisplayName'); DisplayVersion = $key.GetValue('DisplayVersion') } } finally { $key.Dispose() } }
        } finally { $root.Dispose() }
    }
}
if (UninstallMetadata) { throw 'An installed copy exists. Use a clean Windows account or runner for this test.' }
$originalShortcut = if (Test-Path -LiteralPath $shortcutPath) { [IO.File]::ReadAllBytes($shortcutPath) } else { $null }
$registry = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKey)
$originalStartup = $registry.GetValue('PowerShift', $null)
$registry.Dispose()
$originalSettings = if (Test-Path -LiteralPath $settingsPath) { [IO.File]::ReadAllText($settingsPath) } else { $null }
$originalApplications = @(Get-Process -Name PowerShift -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path -Unique)
$ownedApp = $null
$retainedSettings = $null
$logMarker = $null
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw "FAIL: $message" }
    Write-Output "PASS: $message"
}
function RunInstaller([string]$tasks) {
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $destination + '"'),('/TASKS="' + $tasks + '"')) -WindowStyle Hidden -PassThru -Wait
    Check ($process.ExitCode -eq 0) ('installer exit code: ' + $process.ExitCode)
}
function ReadStartup {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKey)
    try { $key.GetValue('PowerShift', $null) } finally { $key.Dispose() }
}
try {
    RunInstaller 'startup'
    $app = Join-Path $destination 'PowerShift.exe'
    Check (Test-Path -LiteralPath $app) 'app copied to selected user directory'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    Check ($shortcut.TargetPath -eq $app -and $shortcut.Arguments -eq '--settings') 'Start menu shortcut points to installed app'
    Check ((ReadStartup) -eq ('"' + $app + '"')) 'selected startup task registers installed path'
    $metadata = UninstallMetadata
    Check ($metadata.DisplayName -eq 'PowerShift' -and $metadata.DisplayVersion -eq '0.4.1') 'Windows Installed Apps registration created'
    $ownedApp = Start-Process -FilePath $app -WindowStyle Hidden -PassThru
    Check (!$ownedApp.WaitForExit(3000)) 'installed app launches'
    $retainedSettings = [IO.File]::ReadAllText($settingsPath)
    $logDirectory = Join-Path $env:APPDATA 'PowerShift\logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $logMarker = Join-Path $logDirectory ('installer-test-' + [Guid]::NewGuid().ToString('N') + '.keep')
    [IO.File]::WriteAllText($logMarker, 'Preserve user logs')
    RunInstaller ''
    Check ($ownedApp.WaitForExit(5000)) 'upgrade gracefully closes running app'
    $ownedApp.Dispose(); $ownedApp = $null
    Check ($null -eq (ReadStartup)) 'deselected startup task removes stale entry on upgrade'
    Check (Test-Path -LiteralPath $app) 'in-place upgrade retains executable'
    $ownedApp = Start-Process -FilePath $app -WindowStyle Hidden -PassThru
    Check (!$ownedApp.WaitForExit(3000)) 'upgraded app launches'
    $enable = Start-Process -FilePath $app -ArgumentList '--enable-startup' -WindowStyle Hidden -PassThru -Wait
    Check ($enable.ExitCode -eq 0 -and (ReadStartup) -eq ('"' + $app + '"')) 'app can enable startup after installation'
    $uninstaller = Join-Path $destination 'unins000.exe'
    $remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru -Wait
    Check ($remove.ExitCode -eq 0) 'uninstaller completes'
    Check ($ownedApp.WaitForExit(5000)) 'uninstaller gracefully closes app'
    $ownedApp.Dispose(); $ownedApp = $null
    Check (!(Test-Path -LiteralPath $app)) 'installed executable removed'
    Check (!(Test-Path -LiteralPath $shortcutPath)) 'Start menu shortcut removed'
    Check ($null -eq (ReadStartup)) 'startup enabled by app is removed on uninstall'
    Check ($null -eq (UninstallMetadata)) 'Windows Installed Apps registration removed'
    Check ([IO.File]::ReadAllText($settingsPath) -eq $retainedSettings) 'settings preserved across upgrade and uninstall'
    Check ((Test-Path -LiteralPath $logMarker) -and [IO.File]::ReadAllText($logMarker) -eq 'Preserve user logs') 'user log directory preserved on uninstall'
} finally {
    if ($ownedApp -and !$ownedApp.HasExited) {
        $exit = Start-Process -FilePath (Join-Path $destination 'PowerShift.exe') -ArgumentList '--exit' -WindowStyle Hidden -PassThru -Wait
        if (!$ownedApp.WaitForExit(5000)) { throw 'Test app still running; leaving files intact.' }
    }
    $remainingUninstaller = Join-Path $destination 'unins000.exe'
    if ((UninstallMetadata) -and (Test-Path -LiteralPath $remainingUninstaller)) {
        $cleanup = Start-Process -FilePath $remainingUninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru -Wait
        if ($cleanup.ExitCode -ne 0) { throw 'Test installation cleanup failed.' }
    }
    $registry = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKey)
    try {
        if ($null -ne $originalStartup) { $registry.SetValue('PowerShift', $originalStartup, [Microsoft.Win32.RegistryValueKind]::String) }
        else { $registry.DeleteValue('PowerShift', $false) }
    } finally { $registry.Dispose() }
    if ($null -ne $originalShortcut) { [IO.File]::WriteAllBytes($shortcutPath, $originalShortcut) }
    if ($logMarker -and (Test-Path -LiteralPath $logMarker)) { Remove-Item -LiteralPath $logMarker }
    foreach ($originalApp in $originalApplications) {
        $stillRunning = @(Get-Process -Name PowerShift -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $originalApp })
        if (!$stillRunning -and (Test-Path -LiteralPath $originalApp)) { Start-Process -FilePath $originalApp -WindowStyle Hidden | Out-Null }
    }
    Write-Output 'Restored previous startup registration, shortcut, and running app.'
}
