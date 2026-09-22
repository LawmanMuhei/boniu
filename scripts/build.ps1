[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputRoot = '',
    [switch]$TestsOnly,
    [switch]$SkipSigning,
    [switch]$RequireSigning,
    [ValidateSet('x64', 'x86')]
    [string]$Platform = 'x64',
    [string]$SignPfxPath = $env:BONIU_SIGN_PFX,
    [string]$SignPfxPassword = $env:BONIU_SIGN_PASSWORD,
    [string]$SignCertificateThumbprint = $env:BONIU_SIGN_THUMBPRINT
)

$ErrorActionPreference = 'Stop'
if ($RequireSigning -and ($SkipSigning -or $TestsOnly)) { throw 'RequireSigning conflicts with validation/no-signing mode.' }
if ($RequireSigning -and [string]::IsNullOrWhiteSpace($SignPfxPath) -and [string]::IsNullOrWhiteSpace($SignCertificateThumbprint)) {
    throw 'RequireSigning needs BONIU_SIGN_PFX or BONIU_SIGN_THUMBPRINT. No certificate was configured.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src'
$versionSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'AppVersion.cs') -Raw -Encoding UTF8
if ($versionSource -notmatch 'Current = "(\d+\.\d+\.\d+)"') { throw 'Missing AppVersion.Current.' }
$appVersion = $Matches[1]
& (Join-Path $PSScriptRoot 'check-version.ps1')
# Validation builds never overwrite release artifacts. Each architecture has its own dependencies.
$outputBase = if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $projectRoot } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputRoot)) }
if ($outputBase -ne $projectRoot -and -not $outputBase.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must stay inside the project.'
}
$buildRoot = Join-Path $outputBase ("build\" + $Platform)
$payloadRoot = Join-Path $buildRoot ("payload-" + $Platform)
$distRoot = Join-Path $outputBase 'dist'
$packageVersion = '1.0.4191.47'
$packagesRoot = Join-Path $projectRoot '.packages'
$packageRoot = Join-Path $packagesRoot "Microsoft.Web.WebView2.$packageVersion"
$packageFile = Join-Path $packagesRoot "Microsoft.Web.WebView2.$packageVersion.nupkg"
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$frameworkRoot = Split-Path -Parent $compiler

function Invoke-CodeSign([string]$FilePath) {
    if ($SkipSigning -or $TestsOnly) { Write-Host "Code signing disabled for validation: $FilePath"; return }
    if ([string]::IsNullOrWhiteSpace($SignPfxPath) -and [string]::IsNullOrWhiteSpace($SignCertificateThumbprint)) {
        Write-Host "Code signing skipped (no certificate configured): $FilePath"
        return
    }
    $signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signTool) {
        $candidate = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\x64\signtool\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $candidate) { throw 'signtool.exe was not found. Install the Windows SDK signing tools.' }
        $signTool = $candidate.FullName
    } else { $signTool = $signTool.Source }
    $signArguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', 'http://timestamp.digicert.com')
    if (-not [string]::IsNullOrWhiteSpace($SignPfxPath)) {
        $signArguments += @('/f', $SignPfxPath)
        if (-not [string]::IsNullOrEmpty($SignPfxPassword)) { $signArguments += @('/p', $SignPfxPassword) }
    } else {
        $signArguments += @('/sha1', $SignCertificateThumbprint)
    }
    $signArguments += $FilePath
    & $signTool @signArguments
    if ($LASTEXITCODE -ne 0) { throw "Code signing failed with exit code ${LASTEXITCODE}: $FilePath" }
    $signature = Get-AuthenticodeSignature -LiteralPath $FilePath
    if ($signature.Status -ne 'Valid') { throw "Signature verification failed: $($signature.Status)" }
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.x C# compiler was not found.'
}

if (-not (Test-Path -LiteralPath $packageRoot)) {
    New-Item -ItemType Directory -Force -Path $packagesRoot | Out-Null
    if (-not (Test-Path -LiteralPath $packageFile)) {
        $packageUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$packageVersion/microsoft.web.webview2.$packageVersion.nupkg"
        Invoke-WebRequest -Uri $packageUrl -OutFile $packageFile -TimeoutSec 120
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packageFile, $packageRoot)
}

if (Test-Path -LiteralPath $buildRoot) {
    $resolvedBuild = (Resolve-Path -LiteralPath $buildRoot).Path
    $resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path
    if (-not $resolvedBuild.StartsWith($resolvedProject + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected build path: $resolvedBuild"
    }
    Remove-Item -LiteralPath $resolvedBuild -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $payloadRoot, $distRoot | Out-Null
$manifestPath = Join-Path $buildRoot 'app.manifest'
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'app.manifest') -Raw -Encoding UTF8
$manifest = [regex]::Replace($manifest, '(assemblyIdentity version=")[^"]+', ('${1}' + $appVersion + '.0'))
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

$webViewLib = Join-Path $packageRoot 'lib\net462'
$nativeLoader = Join-Path $packageRoot ("runtimes\win-" + $Platform + "\native\WebView2Loader.dll")
$iconFile = Join-Path $projectRoot 'assets\boniu-moyu.ico'
$appDisplayName = ([char]0x6CE2).ToString() + ([char]0x599E).ToString() + ([char]0x6478).ToString() + ([char]0x9C7C).ToString()
$architectureSuffix = if ($Platform -eq 'x86') { '-x86' } else { '' }
$appExecutable = Join-Path $payloadRoot ($appDisplayName + '.exe')
$testExecutable = Join-Path $buildRoot ($appDisplayName + '.Tests' + $architectureSuffix + '.exe')
$finalExecutable = Join-Path $distRoot ($appDisplayName + $architectureSuffix + '.exe')
$references = @(
    (Join-Path $frameworkRoot 'System.dll'),
    (Join-Path $frameworkRoot 'System.Core.dll'),
    (Join-Path $frameworkRoot 'System.Drawing.dll'),
    (Join-Path $frameworkRoot 'System.Net.Http.dll'),
    (Join-Path $frameworkRoot 'System.Web.Extensions.dll'),
    (Join-Path $frameworkRoot 'System.Windows.Forms.dll'),
    (Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll')
)
$facadeRuntime = Join-Path $frameworkRoot 'Facades\System.Runtime.dll'
if (Test-Path -LiteralPath $facadeRuntime) { $references += $facadeRuntime }
$referenceArguments = $references | ForEach-Object { "/reference:$_" }
$appSources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | Where-Object { $_.Name -ne 'Bootstrap.cs' } | Sort-Object Name | ForEach-Object { $_.FullName })

& $compiler /nologo /codepage:65001 /optimize+ /debug- "/platform:$Platform" /target:winexe `
    "/win32icon:$iconFile" `
    "/win32manifest:$manifestPath" "/out:$appExecutable" `
    $referenceArguments $appSources
if ($LASTEXITCODE -ne 0) { throw "Application compilation failed with exit code $LASTEXITCODE." }
Invoke-CodeSign $appExecutable

& $compiler /nologo /codepage:65001 /optimize+ /debug- "/platform:$Platform" /target:exe `
    "/win32manifest:$manifestPath" "/out:$testExecutable" `
    $referenceArguments $appSources
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination "$appExecutable.config" -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll') -Destination $payloadRoot -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll') -Destination $payloadRoot -Force
Copy-Item -LiteralPath $nativeLoader -Destination $payloadRoot -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll') -Destination $buildRoot -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll') -Destination $buildRoot -Force
Copy-Item -LiteralPath $nativeLoader -Destination $buildRoot -Force

if ($TestsOnly) {
    & $testExecutable --self-test
    if ($LASTEXITCODE -ne 0) { throw "Self-tests failed: $LASTEXITCODE" }
    Write-Host "TEST_EXECUTABLE=$testExecutable"
    return
}

$bootstrapResources = @(
    "/resource:$appExecutable,payload.BoNiuMoYu.exe",
    "/resource:$appExecutable.config,payload.BoNiuMoYu.exe.config",
    "/resource:$(Join-Path $payloadRoot 'Microsoft.Web.WebView2.Core.dll'),payload.Microsoft.Web.WebView2.Core.dll",
    "/resource:$(Join-Path $payloadRoot 'Microsoft.Web.WebView2.WinForms.dll'),payload.Microsoft.Web.WebView2.WinForms.dll",
    "/resource:$(Join-Path $payloadRoot 'WebView2Loader.dll'),payload.WebView2Loader.dll"
)
& $compiler /nologo /codepage:65001 /optimize+ /debug- "/platform:$Platform" /target:winexe `
    "/win32icon:$iconFile" `
    "/win32manifest:$manifestPath" "/out:$finalExecutable" `
    "/reference:$(Join-Path $frameworkRoot 'System.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.Core.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.Windows.Forms.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.IO.Compression.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.IO.Compression.FileSystem.dll')" `
    $bootstrapResources (Join-Path $sourceRoot 'AppVersion.cs') (Join-Path $sourceRoot 'AssemblyInfo.cs') `
    (Join-Path $sourceRoot 'CommandLineArguments.cs') (Join-Path $sourceRoot 'Bootstrap.cs')
if ($LASTEXITCODE -ne 0) { throw "Bootstrap compilation failed with exit code $LASTEXITCODE." }
Invoke-CodeSign $finalExecutable

Get-Item -LiteralPath $finalExecutable | Select-Object FullName, Length, LastWriteTime
