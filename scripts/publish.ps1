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

& (Join-Path $PSScriptRoot 'build.ps1') -Platform x64
$x64Executable = Join-Path $projectRoot 'dist\BoniuMoyu.exe'
Copy-Item -LiteralPath (Join-Path $projectRoot 'dist\波妞摸鱼.exe') -Destination $x64Executable -Force
$x64HashFile = $x64Executable + '.sha256'
$x64Hash = (Get-FileHash -LiteralPath $x64Executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($x64HashFile, "$x64Hash  BoniuMoyu.exe`n", [Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot 'build.ps1') -Platform x86
$x86Executable = Join-Path $projectRoot 'dist\BoniuMoyu-x86.exe'
Copy-Item -LiteralPath (Join-Path $projectRoot 'dist\波妞摸鱼-x86.exe') -Destination $x86Executable -Force
$x86HashFile = $x86Executable + '.sha256'
$x86Hash = (Get-FileHash -LiteralPath $x86Executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($x86HashFile, "$x86Hash  BoniuMoyu-x86.exe`n", [Text.UTF8Encoding]::new($false))

$gh = Get-Command gh.exe -ErrorAction SilentlyContinue
if (-not $gh) { throw 'GitHub CLI (gh.exe) was not found. Install it and run gh auth login first.' }
& $gh.Source release create "v$Version" $x64Executable $x64HashFile $x86Executable $x86HashFile --repo 'LawmanMuhei/boniu' --title "波妞摸鱼 v$Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "GitHub release failed with exit code $LASTEXITCODE." }

Write-Host "Published v$Version x64 and x86 executables with SHA-256 verification files."
