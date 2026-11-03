$ErrorActionPreference = 'Stop'
$appPath = (Resolve-Path "$PSScriptRoot\..\bin\PowerShift.exe").Path
$configPath = Join-Path $env:APPDATA 'PowerShift\settings.json'
$originalConfig = if (Test-Path -LiteralPath $configPath) { [IO.File]::ReadAllBytes($configPath) } else { $null }
function ActivePlan {
    $output = & powercfg /getactivescheme
    return [regex]::Match(($output -join ''), '[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}').Value.ToLowerInvariant()
}
$originalPlan = ActivePlan
$targetPlan = if ($originalPlan -ne '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c') { '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' } else { '381b4222-f694-41f0-9685-ff5bb260df2e' }
$app = $first = $second = $other = $null
$testStarted = [DateTimeOffset]::UtcNow
function TestAuditEntries {
    $logPath = Join-Path $env:APPDATA 'PowerShift\logs'
    if (!(Test-Path -LiteralPath $logPath)) { return }
    foreach ($logFile in Get-ChildItem -LiteralPath $logPath -Filter 'PowerShift-*.jsonl') {
        foreach ($line in Get-Content -LiteralPath $logFile.FullName -Encoding UTF8) {
            $entry = $line | ConvertFrom-Json
            if ([DateTimeOffset]::Parse($entry.TimestampUtc) -ge $testStarted -and ($entry.Rule -match '#1: ping.exe' -or $entry.PreviousRule -match '#1: ping.exe')) { $entry }
        }
    }
}
try {
    if (Get-Process -Name PowerShift -ErrorAction SilentlyContinue) { throw 'Stop PowerShift before running this smoke test.' }
    if (Get-Process -Name ping -ErrorAction SilentlyContinue) { throw 'Stop existing ping processes before running this smoke test.' }
    $testConfig = @{ Version = 1; DefaultPlanId = $originalPlan; AutomaticSwitching = $true; Rules = @(@{ Enabled = $true; ProcessName = 'ping'; ExecutablePath = "$env:WINDIR\System32\ping.exe"; PlanId = $targetPlan }) }
    [IO.File]::WriteAllText($configPath, ($testConfig | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    $app = Start-Process -FilePath $appPath -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3
    if ($app.HasExited) { throw 'Tray app exited unexpectedly.' }
    if (Test-Path -LiteralPath "$env:WINDIR\SysWOW64\ping.exe") {
        $other = Start-Process -FilePath "$env:WINDIR\SysWOW64\ping.exe" -ArgumentList '-t 127.0.0.1' -WindowStyle Hidden -PassThru
        Start-Sleep -Seconds 3
        if ($other.HasExited) { throw 'Alternate-path process exited unexpectedly.' }
        if ((ActivePlan) -ne $originalPlan) { throw 'Same EXE name at a different path incorrectly activated profile.' }
        'PASS: same EXE name at different path does not trigger profile'
    }
    $first = Start-Process -FilePath "$env:WINDIR\System32\ping.exe" -ArgumentList '-t 127.0.0.1' -WindowStyle Hidden -PassThru
    $second = Start-Process -FilePath "$env:WINDIR\System32\ping.exe" -ArgumentList '-t 127.0.0.1' -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3
    if ((ActivePlan) -ne $targetPlan) { throw 'Watcher did not apply process profile.' }
    'PASS: running tray watcher detects process and applies profile'
    $audit = @(TestAuditEntries)
    if ($audit.Count -ne 1 -or ![String]::Equals($audit[0].ExecutablePath, "$env:WINDIR\System32\ping.exe", [StringComparison]::OrdinalIgnoreCase)) { throw 'Profile switch audit did not include the matched rule and full path.' }
    'PASS: switch audit includes exact matched rule and executable path'
    Stop-Process -Id $first.Id -Force
    $first.WaitForExit()
    Start-Sleep -Seconds 3
    if ((ActivePlan) -ne $targetPlan) { throw 'Profile ended while second process instance was running.' }
    'PASS: profile stays active while another instance is running'
    if (@(TestAuditEntries).Count -ne 1) { throw 'Unchanged polling added duplicate audit records.' }
    'PASS: unchanged polling does not repeat audit records'
    Stop-Process -Id $second.Id -Force
    $second.WaitForExit()
    Start-Sleep -Seconds 3
    if ((ActivePlan) -ne $originalPlan) { throw 'Watcher did not restore default.' }
    'PASS: last process exit restores default'
    $audit = @(TestAuditEntries)
    if ($audit.Count -ne 2 -or $audit[1].PreviousRule -notmatch '#1: ping.exe' -or $audit[0].FromPlan -ne $audit[1].ToPlan -or $audit[0].ToPlan -ne $audit[1].FromPlan) { throw 'Restore audit is missing the previous rule or before/after power plans.' }
    'PASS: restore audit includes previous rule and reversed before/after plans'
    if ($other -and !$other.HasExited) { 'PASS: nonmatching EXE path does not keep profile active' }
    $exitCommand = Start-Process -FilePath $appPath -ArgumentList '--exit' -WindowStyle Hidden -PassThru -Wait
    if (!$app.WaitForExit(5000)) { throw 'Graceful exit signal was not handled.' }
    'PASS: graceful tray exit'
} finally {
    foreach ($child in @($first, $second, $other)) { if ($child) { if (!$child.HasExited) { Stop-Process -Id $child.Id -Force }; $child.Dispose() } }
    if ($app) { if (!$app.HasExited) { Stop-Process -Id $app.Id -Force }; $app.Dispose() }
    if ($originalConfig) { [IO.File]::WriteAllBytes($configPath, $originalConfig) } elseif (Test-Path -LiteralPath $configPath) { Remove-Item -LiteralPath $configPath }
    & powercfg /setactive $originalPlan
    'Restored original settings and active power plan.'
}
