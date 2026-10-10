[CmdletBinding(SupportsShouldProcess = $true)]
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

# -WhatIf 只跳过上传：本地构建与 dist 产物必须真实生成，因为校验文件里的哈希来自真实文件。
# $WhatIfPreference 是偏好变量，会被 build.ps1 内的 Remove-Item / New-Item / Copy-Item 继承；
# 不抑制的话 -WhatIf 会连编译产物的复制和目录重建一起跳过，留下半新半旧的 dist。
$uploadWhatIf = $WhatIfPreference
$WhatIfPreference = $false
try {
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
} finally {
    $WhatIfPreference = $uploadWhatIf
}

# 自动更新资产用固定文件名，手动下载副本带版本号与架构；两类文件随本次发布一起上传。
$releaseFiles = @($x64Executable, $x64HashFile, $x86Executable, $x86HashFile) + $manualFiles
$releaseTarget = "GitHub release v$Version (LawmanMuhei/boniu)"
$labels = @{
    'BoniuMoyu.exe'            = "自动更新专用 · 64 位（手动下载请选 BoniuMoyu-$Version-x64.exe）"
    'BoniuMoyu-x86.exe'        = "自动更新专用 · 32 位（手动下载请选 BoniuMoyu-$Version-x86.exe）"
    'BoniuMoyu.exe.sha256'     = '自动更新校验文件 · 64 位'
    'BoniuMoyu-x86.exe.sha256' = '自动更新校验文件 · 32 位'
}

if (-not $PSCmdlet.ShouldProcess($releaseTarget, "Upload $($releaseFiles.Count) assets")) {
    # -WhatIf：本地构建与 dist 资产已经真实生成，这里只打印将要上传的目标、资产、大小和哈希，
    # 不联网、也不要求安装 gh。
    Write-Host "WhatIf: 未上传任何文件，本地构建与 dist 资产已生成。$releaseTarget 将包含："
    foreach ($file in $releaseFiles) {
        $item = Get-Item -LiteralPath $file
        if ($item.Extension -eq '.sha256') {
            $recorded = (Get-Content -LiteralPath $file -Raw).Trim()
            Write-Host ("  {0,-34} {1,10} bytes  contains {2}" -f $item.Name, $item.Length, $recorded)
        } else {
            $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
            Write-Host ("  {0,-34} {1,10} bytes  sha256   {2}" -f $item.Name, $item.Length, $hash)
        }
    }
    Write-Host ("WhatIf: 发布后将应用显示标签的资产：{0}" -f (($labels.Keys | Sort-Object) -join '、'))
    return
}

$gh = Get-Command gh.exe -ErrorAction SilentlyContinue
if (-not $gh) { throw 'GitHub CLI (gh.exe) was not found. Install it and run gh auth login first.' }
& $gh.Source release create "v$Version" @releaseFiles --repo 'LawmanMuhei/boniu' --title "波妞摸鱼 v$Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "GitHub release failed with exit code $LASTEXITCODE." }

# 给自动更新资产加上显示标签（label 会替代文件名展示），提醒用户手动下载带版本号的文件。
try
{
    $releaseId = & $gh.Source api "repos/LawmanMuhei/boniu/releases/tags/v$Version" --jq '.id'
    $assetLines = & $gh.Source api "repos/LawmanMuhei/boniu/releases/$releaseId/assets" --jq '.[] | "\(.id) \(.name)"'
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
