[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Notes = '稳定性改进和问题修复。',
    [switch]$RequireSigning
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'check-version.ps1')
$versionSource = Get-Content (Join-Path $projectRoot 'src\AppVersion.cs') -Raw -Encoding UTF8
if ($versionSource -notmatch 'Current = "(\d+\.\d+\.\d+)"' -or $Matches[1] -ne $Version) {
    throw "AppVersion.cs version does not match $Version. Update it and the documentation before publishing."
}

& (Join-Path $PSScriptRoot 'build.ps1') -Platform x64 -RequireSigning:$RequireSigning
$x64Executable = Join-Path $projectRoot 'dist\BoniuMoyu.exe'
Copy-Item -LiteralPath (Join-Path $projectRoot 'dist\波妞摸鱼.exe') -Destination $x64Executable -Force
$x64HashFile = $x64Executable + '.sha256'
$x64Hash = (Get-FileHash -LiteralPath $x64Executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($x64HashFile, "$x64Hash  BoniuMoyu.exe`n", [Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot 'build.ps1') -Platform x86 -RequireSigning:$RequireSigning
$x86Executable = Join-Path $projectRoot 'dist\BoniuMoyu-x86.exe'
Copy-Item -LiteralPath (Join-Path $projectRoot 'dist\波妞摸鱼-x86.exe') -Destination $x86Executable -Force
$x86HashFile = $x86Executable + '.sha256'
$x86Hash = (Get-FileHash -LiteralPath $x86Executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($x86HashFile, "$x86Hash  BoniuMoyu-x86.exe`n", [Text.UTF8Encoding]::new($false))

# 手动下载副本：文件名带版本号与架构，避免用户下错版本；自动更新仍使用不带版本号的固定文件名。
$manualFiles = @()
foreach ($pair in @(@($x64Executable, 'x64', $x64Hash), @($x86Executable, 'x86', $x86Hash)))
{
    $manual = Join-Path $projectRoot ('dist\BoniuMoyu-' + $Version + '-' + $pair[1] + '.exe')
    Copy-Item -LiteralPath $pair[0] -Destination $manual -Force
    $manualHashFile = $manual + '.sha256'
    [IO.File]::WriteAllText($manualHashFile, ($pair[2] + '  ' + [IO.Path]::GetFileName($manual) + "`n"), [Text.UTF8Encoding]::new($false))
    $manualFiles += $manual
    $manualFiles += $manualHashFile
}

$gh = Get-Command gh.exe -ErrorAction SilentlyContinue
if (-not $gh) { throw 'GitHub CLI (gh.exe) was not found. Install it and run gh auth login first.' }
$releaseFiles = @($x64Executable, $x64HashFile, $x86Executable, $x86HashFile) + $manualFiles
& $gh.Source release create "v$Version" @releaseFiles --repo 'LawmanMuhei/boniu' --title "波妞摸鱼 v$Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "GitHub release failed with exit code $LASTEXITCODE." }

# 给自动更新资产加上显示标签（label 会替代文件名展示），提醒用户手动下载带版本号的文件。
try
{
    $releaseId = & $gh.Source api "repos/LawmanMuhei/boniu/releases/tags/v$Version" --jq '.id'
    $assetLines = & $gh.Source api "repos/LawmanMuhei/boniu/releases/$releaseId/assets" --jq '.[] | "\(.id) \(.name)"'
    $labels = @{
        'BoniuMoyu.exe'            = "自动更新专用 · 64 位（手动下载请选 BoniuMoyu-$Version-x64.exe）"
        'BoniuMoyu-x86.exe'        = "自动更新专用 · 32 位（手动下载请选 BoniuMoyu-$Version-x86.exe）"
        'BoniuMoyu.exe.sha256'     = '自动更新校验文件 · 64 位'
        'BoniuMoyu-x86.exe.sha256' = '自动更新校验文件 · 32 位'
    }
    foreach ($line in $assetLines)
    {
        $parts = $line -split ' ', 2
        if ($parts.Count -eq 2 -and $labels.ContainsKey($parts[1]))
        {
            & $gh.Source api -X PATCH "repos/LawmanMuhei/boniu/releases/assets/$($parts[0])" -f "label=$($labels[$parts[1]])" | Out-Null
            Write-Host "Labelled $($parts[1])"
        }
    }
}
catch
{
    Write-Warning "Release assets were uploaded but labels could not be applied: $($_.Exception.Message)"
}

Write-Host "Published v$Version x64 and x86 executables, each with a versioned manual-download copy and SHA-256 verification files."
