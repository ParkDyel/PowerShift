param([switch]$Integration)
$ErrorActionPreference = 'Stop'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$sources += Join-Path $PSScriptRoot 'tests\Tests.cs'
Add-Type -Path $sources -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Web.Extensions,System.Core
exit [PowerShift.Tests]::Run($Integration.IsPresent)
