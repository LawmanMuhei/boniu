[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Notes = '稳定性改进和问题修复。'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assemblyInfo = Get-Content (Join-Path $projectRoot 'src\AssemblyInfo.cs') -Raw
if ($assemblyInfo -notmatch [regex]::Escape("AssemblyVersion(`"$Version.0`")")) {
    throw "AssemblyInfo.cs version does not match $Version. Update it before publishing."
}

& (Join-Path $PSScriptRoot 'build.ps1')
$executable = Join-Path $projectRoot 'dist\波妞摸鱼.exe'
$hashFile = $executable + '.sha256'
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($hashFile, "$hash  波妞摸鱼.exe`n", [Text.UTF8Encoding]::new($false))

$gh = Get-Command gh.exe -ErrorAction SilentlyContinue
if (-not $gh) { throw 'GitHub CLI (gh.exe) was not found. Install it and run gh auth login first.' }
& $gh.Source release create "v$Version" $executable $hashFile --repo 'LawmanMuhei/boniu' --title "波妞摸鱼 v$Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "GitHub release failed with exit code $LASTEXITCODE." }

Write-Host "Published v$Version with executable and SHA-256 verification file."
