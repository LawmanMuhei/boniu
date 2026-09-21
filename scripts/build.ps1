[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$SignPfxPath = $env:BONIU_SIGN_PFX,
    [string]$SignPfxPassword = $env:BONIU_SIGN_PASSWORD,
    [string]$SignCertificateThumbprint = $env:BONIU_SIGN_THUMBPRINT
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src'
$buildRoot = Join-Path $projectRoot 'build'
$payloadRoot = Join-Path $buildRoot 'payload'
$distRoot = Join-Path $projectRoot 'dist'
$packageVersion = '1.0.4191.47'
$packagesRoot = Join-Path $projectRoot '.packages'
$packageRoot = Join-Path $packagesRoot "Microsoft.Web.WebView2.$packageVersion"
$packageFile = Join-Path $packagesRoot "Microsoft.Web.WebView2.$packageVersion.nupkg"
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkRoot = Split-Path -Parent $compiler

function Invoke-CodeSign([string]$FilePath) {
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

$webViewLib = Join-Path $packageRoot 'lib\net462'
$nativeLoader = Join-Path $packageRoot 'runtimes\win-x64\native\WebView2Loader.dll'
$iconFile = Join-Path $projectRoot 'assets\boniu-moyu.ico'
$appDisplayName = ([char]0x6CE2).ToString() + ([char]0x599E).ToString() + ([char]0x6478).ToString() + ([char]0x9C7C).ToString()
$appExecutable = Join-Path $payloadRoot ($appDisplayName + '.exe')
$testExecutable = Join-Path $buildRoot ($appDisplayName + '.Tests.exe')
$finalExecutable = Join-Path $distRoot ($appDisplayName + '.exe')
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
$appSources = @(
    (Join-Path $sourceRoot 'AssemblyInfo.cs'),
    (Join-Path $sourceRoot 'Diagnostics.cs'),
    (Join-Path $sourceRoot 'Logic.cs'),
    (Join-Path $sourceRoot 'SettingsStore.cs'),
    (Join-Path $sourceRoot 'UpdateService.cs'),
    (Join-Path $sourceRoot 'MainForm.cs'),
    (Join-Path $sourceRoot 'Program.cs')
)

& $compiler /nologo /codepage:65001 /optimize+ /debug- /platform:x64 /target:winexe `
    "/win32icon:$iconFile" `
    "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" "/out:$appExecutable" `
    $referenceArguments $appSources
if ($LASTEXITCODE -ne 0) { throw "Application compilation failed with exit code $LASTEXITCODE." }
Invoke-CodeSign $appExecutable

& $compiler /nologo /codepage:65001 /optimize+ /debug- /platform:x64 /target:exe `
    "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" "/out:$testExecutable" `
    $referenceArguments $appSources
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination "$appExecutable.config" -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll') -Destination $payloadRoot -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll') -Destination $payloadRoot -Force
Copy-Item -LiteralPath $nativeLoader -Destination $payloadRoot -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll') -Destination $buildRoot -Force
Copy-Item -LiteralPath (Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll') -Destination $buildRoot -Force
Copy-Item -LiteralPath $nativeLoader -Destination $buildRoot -Force

$bootstrapResources = @(
    "/resource:$appExecutable,payload.BoNiuMoYu.exe",
    "/resource:$appExecutable.config,payload.BoNiuMoYu.exe.config",
    "/resource:$(Join-Path $payloadRoot 'Microsoft.Web.WebView2.Core.dll'),payload.Microsoft.Web.WebView2.Core.dll",
    "/resource:$(Join-Path $payloadRoot 'Microsoft.Web.WebView2.WinForms.dll'),payload.Microsoft.Web.WebView2.WinForms.dll",
    "/resource:$(Join-Path $payloadRoot 'WebView2Loader.dll'),payload.WebView2Loader.dll"
)
& $compiler /nologo /codepage:65001 /optimize+ /debug- /platform:x64 /target:winexe `
    "/win32icon:$iconFile" `
    "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" "/out:$finalExecutable" `
    "/reference:$(Join-Path $frameworkRoot 'System.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.Core.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.Windows.Forms.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.IO.Compression.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.IO.Compression.FileSystem.dll')" `
    $bootstrapResources (Join-Path $sourceRoot 'AssemblyInfo.cs') (Join-Path $sourceRoot 'Bootstrap.cs')
if ($LASTEXITCODE -ne 0) { throw "Bootstrap compilation failed with exit code $LASTEXITCODE." }
Invoke-CodeSign $finalExecutable

Get-Item -LiteralPath $finalExecutable | Select-Object FullName, Length, LastWriteTime
