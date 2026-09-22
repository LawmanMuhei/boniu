[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')][string]$Platform = 'x64',
    [string]$OutputRoot = 'artifacts/verification',
    [switch]$SkipBuild,
    [switch]$SkipRegression,
    [int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Platform $Platform -OutputRoot $OutputRoot -TestsOnly -SkipSigning
}
$root = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputRoot))
if (-not $root.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test output must stay inside the project.' }
$suffix = if ($Platform -eq 'x86') { '-x86' } else { '' }
$name = ([char]0x6CE2).ToString() + ([char]0x599E).ToString() + ([char]0x6478).ToString() + ([char]0x9C7C).ToString()
$exe = Join-Path $root ("build\$Platform\$name.Tests$suffix.exe")
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing test executable: $exe" }
$cases = @('--self-test', '--smoke-test', '--live-smoke-test', '--settings-smoke-test')
if (-not $SkipRegression) { $cases += '--regression-test' }
$results = @()
foreach ($case in $cases) {
    $start = [Diagnostics.ProcessStartInfo]::new($exe, $case)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = Split-Path -Parent $exe
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $testPid = $process.Id
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        # Only terminate the exact test process started by this runner; never target a name/user app.
        $process.Kill()
        $process.WaitForExit()
    }
    $exitCode = if ($timedOut) { -1 } else { $process.ExitCode }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $timer.Stop()
    $testData = Join-Path ([IO.Path]::GetTempPath()) "MiniViewWebView2Smoke\$testPid"
    $record = [ordered]@{ case = $case; platform = $Platform; exitCode = $exitCode; timedOut = $timedOut; elapsedMs = $timer.ElapsedMilliseconds; stdout = $stdout; stderr = $stderr; dataDirectory = $testData }
    $results += [pscustomobject]$record
    Write-Host "$case exit=$exitCode elapsed=$($timer.ElapsedMilliseconds)ms"
    if ($stdout) { Write-Host $stdout.Trim() }
    if ($stderr) { Write-Host $stderr.Trim() }
    $report = Join-Path $testData 'regression.json'
    if (Test-Path -LiteralPath $report) { Copy-Item -LiteralPath $report -Destination (Join-Path $root "regression-$Platform.json") -Force }
    $image = Join-Path $testData 'settings-fixture.png'
    if (Test-Path -LiteralPath $image) { Copy-Item -LiteralPath $image -Destination (Join-Path $root "settings-$Platform.png") -Force }
    $process.Dispose()
}
$reportPath = Join-Path $root "tests-$Platform.json"
[IO.File]::WriteAllText($reportPath, (ConvertTo-Json -InputObject $results -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "REPORT=$reportPath"
if (@($results | Where-Object { $_.exitCode -ne 0 }).Count -gt 0) { exit 1 }
exit 0
