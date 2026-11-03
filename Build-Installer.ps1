param(
    [string]$IsccPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.4.1',
    [switch]$UseExistingBinary
)
$ErrorActionPreference = 'Stop'
if (!$UseExistingBinary) { & (Join-Path $PSScriptRoot 'Build.ps1') | Out-Null }
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'bin\PowerShift.exe'))) { throw 'Build PowerShift.exe first.' }
if (!$IsccPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $IsccPath = $compilerCommand.Source }
    foreach ($candidate in @("${env:ProgramFiles}\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")) {
        if (!$IsccPath -and (Test-Path -LiteralPath $candidate)) { $IsccPath = $candidate }
    }
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) { throw 'Install Inno Setup 6.5+ or pass -IsccPath to ISCC.exe.' }
& $IsccPath '/Qp' "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'installer\PowerShift.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Get-Item -LiteralPath (Join-Path $PSScriptRoot "dist\PowerShift-Setup-$Version.exe")
