param([switch]$EnableStartup, [switch]$DisableStartup, [switch]$Settings)
$ErrorActionPreference = 'Stop'
try {
    $sourceFiles = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName
    Add-Type -Path $sourceFiles -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Web.Extensions
    [AppDomain]::CurrentDomain.SetData('PowerShift.ScriptPath', $PSCommandPath)
    $appArguments = [string[]]@()
    if ($EnableStartup) { $appArguments += '--enable-startup' }
    if ($DisableStartup) { $appArguments += '--disable-startup' }
    if ($Settings) { $appArguments += '--settings' }
    [PowerShift.Program]::Main($appArguments)
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'PowerShift', 'OK', 'Error') | Out-Null
    exit 1
}
