param([switch]$Integration)
$ErrorActionPreference = 'Stop'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$sources += Join-Path $PSScriptRoot 'tests\Tests.cs'
Add-Type -Path $sources -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Web.Extensions,System.Core
$result = [PowerShift.Tests]::Run($Integration.IsPresent)
if ($result -ne 0) { exit $result }
$redirectRoot = Join-Path ([IO.Path]::GetTempPath()) ('PowerShiftRedirectTests-' + [Guid]::NewGuid().ToString('N'))
$outside = Join-Path $redirectRoot 'outside'
$logLink = Join-Path $redirectRoot 'log-link'
$dataLink = Join-Path $redirectRoot 'data-link'
try {
    New-Item -ItemType Directory -Path $outside -Force | Out-Null
    New-Item -ItemType Junction -Path $logLink -Target $outside | Out-Null
    New-Item -ItemType Junction -Path $dataLink -Target $outside | Out-Null
    $result = [PowerShift.Tests]::RunRedirectChecks($redirectRoot)
} finally {
    # Remove only these test-created junctions; never recurse into their targets.
    foreach ($link in @($logLink, $dataLink)) {
        if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    }
    if (Test-Path -LiteralPath $outside) {
        foreach ($file in [IO.Directory]::GetFiles($outside)) { [IO.File]::Delete($file) }
        [IO.Directory]::Delete($outside)
    }
    if (Test-Path -LiteralPath $redirectRoot) { [IO.Directory]::Delete($redirectRoot) }
}
exit $result
