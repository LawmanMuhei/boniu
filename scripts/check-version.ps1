[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\AppVersion.cs') -Raw -Encoding UTF8
if ($source -notmatch 'Current = "(\d+\.\d+\.\d+)"') { throw 'Missing AppVersion.Current.' }
$version = $Matches[1]
$assemblyInfo = Get-Content -LiteralPath (Join-Path $root 'src\AssemblyInfo.cs') -Raw -Encoding UTF8
$bootstrap = Get-Content -LiteralPath (Join-Path $root 'src\Bootstrap.cs') -Raw -Encoding UTF8
if (-not $assemblyInfo.Contains('AssemblyVersion(MiniView.WebView2App.AppVersion.Assembly)') -or
    -not $assemblyInfo.Contains('AssemblyFileVersion(MiniView.WebView2App.AppVersion.Assembly)')) { throw 'Assembly metadata must reference AppVersion.' }
if (-not $bootstrap.Contains('Version = MiniView.WebView2App.AppVersion.Current')) { throw 'Bootstrap must reference AppVersion.' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'app.manifest') -Raw -Encoding UTF8
if ($manifest.assembly.assemblyIdentity.version -ne "$version.0") { throw 'app.manifest version does not match AppVersion.' }
$readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw -Encoding UTF8
if (-not $readme.Contains("-Version $version") -or -not $readme.Contains(("App\" + $version + "-"))) { throw 'README version examples are out of date.' }
$name = ([char]0x6CE2).ToString() + ([char]0x599E).ToString() + ([char]0x6478).ToString() + ([char]0x9C7C).ToString()
$guide = Get-ChildItem -LiteralPath $root -Filter "$name-*.txt" | Select-Object -First 1
if (-not $guide) { throw 'User guide is missing.' }
$text = Get-Content -LiteralPath $guide.FullName -Raw -Encoding UTF8
if (-not $text.Contains("$name $version") -or -not $text.Contains('x86')) { throw 'User guide version or architecture is out of date.' }
Write-Host "Version consistency OK: $version"
