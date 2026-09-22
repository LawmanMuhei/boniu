using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;

namespace MiniView.WebView2App
{
    internal sealed class UpdateInfo
    {
        internal Version Version;
        internal string Tag;
        internal string Name;
        internal string Notes;
        internal string PageUrl;
        internal string AssetUrl;
        internal string HashUrl;
    }

    internal sealed class PreparedUpdate
    {
        internal UpdateInfo Info;
        internal string ExecutablePath;
    }

    internal sealed class UpdateService : IDisposable
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/LawmanMuhei/boniu/releases/latest";
        private const long MaximumExecutableBytes = 50L * 1024L * 1024L;
        private const int SettingsBackupRetention = 3;
        private readonly string appFolder;
        private readonly string launcherPath;
        private readonly string executableAssetName;
        private readonly HttpClient client;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();

        internal UpdateService(string appFolder, HttpMessageHandler handler = null)
        {
            this.appFolder = appFolder;
            launcherPath = Environment.GetEnvironmentVariable("BONIU_LAUNCHER_PATH");
            executableAssetName = GetExecutableAssetName(IntPtr.Size);
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BoniuMoyu/" + CurrentVersion.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        internal static Version CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        internal async Task<UpdateInfo> CheckAsync()
        {
            using (HttpResponseMessage response = await client.GetAsync(LatestReleaseApi, shutdown.Token))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                GitHubRelease release = new JavaScriptSerializer().Deserialize<GitHubRelease>(json);
                if (release == null || release.draft || release.prerelease) return null;
                Version version;
                if (!TryParseVersion(release.tag_name, out version) || version.CompareTo(CurrentVersion) <= 0) return null;

                string executableUrl = null;
                string hashUrl = null;
                if (release.assets != null)
                {
                    foreach (GitHubAsset asset in release.assets)
                    {
                        if (asset == null) continue;
                        if (string.Equals(asset.name, executableAssetName, StringComparison.OrdinalIgnoreCase))
                            executableUrl = asset.browser_download_url;
                        if (string.Equals(asset.name, executableAssetName + ".sha256", StringComparison.OrdinalIgnoreCase))
                            hashUrl = asset.browser_download_url;
                    }
                }
                return new UpdateInfo
                {
                    Version = version,
                    Tag = release.tag_name,
                    Name = release.name,
                    Notes = release.body,
                    PageUrl = release.html_url,
                    AssetUrl = executableUrl,
                    HashUrl = hashUrl
                };
            }
        }

        internal async Task<PreparedUpdate> DownloadAsync(UpdateInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.AssetUrl) || string.IsNullOrEmpty(info.HashUrl))
                throw new InvalidOperationException("此版本缺少 " + executableAssetName + " 或 SHA-256 校验文件，已取消更新。");
            EnsureHttps(info.AssetUrl);
            EnsureHttps(info.HashUrl);

            if (info.Version == null) throw new InvalidDataException("更新版本号缺失。");
            using (CancellationTokenSource transfer = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token))
            {
                // HttpClient.Timeout does not bound body reads with ResponseHeadersRead.
                transfer.CancelAfter(TimeSpan.FromMinutes(3));
                CancellationToken token = transfer.Token;
                string hashText = await DownloadTextLimitedAsync(info.HashUrl, 4096, token).ConfigureAwait(false);
                string expectedHash = ExtractSha256(hashText);
                if (expectedHash == null) throw new InvalidDataException("版本校验文件格式不正确。");

                string updateFolder = Path.Combine(appFolder, "Updates", info.Version.ToString(3));
                Directory.CreateDirectory(updateFolder);
                string stagedPath = Path.Combine(updateFolder, executableAssetName);
                string partialPath = stagedPath + ".part";
                try
                {
                    using (HttpResponseMessage response = await client.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        long? length = response.Content.Headers.ContentLength;
                        if (length.HasValue && length.Value > MaximumExecutableBytes)
                            throw new InvalidDataException("更新文件超过允许的大小。");
                        using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            await CopyLimitedAsync(input, output, MaximumExecutableBytes, token).ConfigureAwait(false);
                    }
                    token.ThrowIfCancellationRequested();
                    if (!string.Equals(expectedHash, ComputeSha256(partialPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("更新文件 SHA-256 校验失败，已取消安装。");
                    if (File.Exists(stagedPath)) File.Delete(stagedPath);
                    File.Move(partialPath, stagedPath);
                    return new PreparedUpdate { Info = info, ExecutablePath = stagedPath };
                }
                finally
                {
                    try { if (File.Exists(partialPath)) File.Delete(partialPath); }
                    catch (Exception exception) { Diagnostics.LogException("UpdatePartialCleanup", exception); }
                }
            }
        }

        public void Dispose()
        {
            shutdown.Cancel();
            client.Dispose();
            shutdown.Dispose();
        }

        internal void BeginInstall(PreparedUpdate prepared, int oldProcessId)
        {
            if (prepared == null || !File.Exists(prepared.ExecutablePath)) throw new FileNotFoundException("找不到已下载的更新文件。");
            if (string.IsNullOrEmpty(launcherPath) || !File.Exists(launcherPath))
                throw new InvalidOperationException("无法确定启动程序位置，请从正式发布的波妞摸鱼.exe 启动后重试。");
            ProbeLauncherDirectory();
            BackupSettings();

            string token = Guid.NewGuid().ToString("N");
            string marker = GetHealthMarkerPath(token);
            string backup = launcherPath + ".bak";
            string logPath = Path.Combine(appFolder, "logs", "update-install.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            string scriptPath = Path.Combine(Path.GetDirectoryName(prepared.ExecutablePath), "install-update.ps1");
            File.WriteAllText(scriptPath, BuildInstallScript(oldProcessId, launcherPath, prepared.ExecutablePath,
                backup, marker, token, logPath, scriptPath), Encoding.Unicode);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + QuoteCommandArgument(scriptPath);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            Process.Start(startInfo);
        }

        private void ProbeLauncherDirectory()
        {
            string probe = Path.Combine(Path.GetDirectoryName(launcherPath), ".boniu-update-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllText(probe, "ok", Encoding.ASCII); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }

        private void BackupSettings()
        {
            string settingsPath = Path.Combine(appFolder, "settings.json");
            if (!File.Exists(settingsPath)) return;
            string backupFolder = Path.Combine(appFolder, "Backups");
            Directory.CreateDirectory(backupFolder);
            string backupPath = Path.Combine(backupFolder, "settings-before-update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
            File.Copy(settingsPath, backupPath, false);
            RetainNewestFiles(backupFolder, "settings-before-update-*.json", SettingsBackupRetention);
        }

        internal static void RetainNewestFiles(string folder, string pattern, int keepCount)
        {
            if (keepCount < 0) throw new ArgumentOutOfRangeException("keepCount");
            if (!Directory.Exists(folder)) return;
            string[] files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
            Array.Sort(files, delegate(string left, string right)
            {
                return File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left));
            });
            for (int i = keepCount; i < files.Length; i++)
            {
                try { File.Delete(files[i]); }
                catch (Exception exception) { Diagnostics.LogException("UpdateBackupCleanup", exception); }
            }
        }

        internal static string BuildInstallScript(int oldProcessId, string target, string staged, string backup,
            string marker, string token, string logPath, string scriptPath)
        {
            Func<string, string> q = PowerShellLiteral;
            StringBuilder script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("$target = " + q(target));
            script.AppendLine("$staged = " + q(staged));
            script.AppendLine("$backup = " + q(backup));
            script.AppendLine("$marker = " + q(marker));
            script.AppendLine("$healthToken = " + q(token));
            script.AppendLine("$log = " + q(logPath));
            script.AppendLine("$backupReady = $false");
            script.AppendLine("try {");
            script.AppendLine("  $exitDeadline = (Get-Date).AddSeconds(30)");
            script.AppendLine("  while ((Get-Date) -lt $exitDeadline -and (Get-Process -Id " + oldProcessId + " -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 200 }");
            script.AppendLine("  if (Get-Process -Id " + oldProcessId + " -ErrorAction SilentlyContinue) { throw 'Old version did not exit.' }");
            script.AppendLine("  Start-Sleep -Milliseconds 400");
            script.AppendLine("  Copy-Item -LiteralPath $target -Destination $backup -Force");
            script.AppendLine("  $backupReady = $true");
            script.AppendLine("  Copy-Item -LiteralPath $staged -Destination $target -Force");
            script.AppendLine("  Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue");
            script.AppendLine("  $process = Start-Process -FilePath $target -ArgumentList @('--update-health-token', $healthToken) -PassThru");
            script.AppendLine("  $deadline = (Get-Date).AddSeconds(30)");
            script.AppendLine("  while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $marker)) { Start-Sleep -Milliseconds 250 }");
            script.AppendLine("  if (-not (Test-Path -LiteralPath $marker)) { throw 'New version did not report healthy startup.' }");
            script.AppendLine("  Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue");
            script.AppendLine("  Add-Content -LiteralPath $log -Value ((Get-Date).ToString('s') + ' update succeeded') -Encoding UTF8");
            script.AppendLine("  Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue");
            script.AppendLine("} catch {");
            script.AppendLine("  Add-Content -LiteralPath $log -Value ((Get-Date).ToString('s') + ' update failed: ' + $_.Exception.Message) -Encoding UTF8");
            script.AppendLine("  Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Contains($healthToken) } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }");
            script.AppendLine("  Start-Sleep -Milliseconds 500");
            script.AppendLine("  if ($backupReady -and (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $backup -Destination $target -Force }");
            script.AppendLine("  Start-Process -FilePath $target");
            script.AppendLine("}");
            script.AppendLine("Remove-Item -LiteralPath " + q(scriptPath) + " -Force -ErrorAction SilentlyContinue");
            return script.ToString();
        }

        internal static void MarkHealthy(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 64) return;
            foreach (char c in token) if (!Uri.IsHexDigit(c)) return;
            try { File.WriteAllText(GetHealthMarkerPath(token), CurrentVersion.ToString(), Encoding.ASCII); }
            catch (Exception exception) { Diagnostics.LogException("MarkUpdateHealthy", exception); }
        }

        internal static string GetHealthMarkerPath(string token)
        {
            return Path.Combine(Path.GetTempPath(), "boniu-update-" + token + ".healthy");
        }

        internal static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string normalized = value.Trim().TrimStart('v', 'V');
            int suffix = normalized.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) normalized = normalized.Substring(0, suffix);
            int parts = normalized.Split('.').Length;
            if (parts == 1) normalized += ".0.0";
            else if (parts == 2) normalized += ".0";
            Version parsed;
            if (!Version.TryParse(normalized, out parsed) || parsed.Major < 0 || parsed.Minor < 0 || parsed.Build < 0) return false;
            version = new Version(parsed.Major, parsed.Minor, parsed.Build);
            return true;
        }

        internal static string GetExecutableAssetName(int pointerSize)
        {
            return pointerSize == 4 ? "BoniuMoyu-x86.exe" : "BoniuMoyu.exe";
        }

        internal static string ExtractSha256(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string first = text.Trim().Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (first.Length != 64) return null;
            foreach (char c in first) if (!Uri.IsHexDigit(c)) return null;
            return first.ToLowerInvariant();
        }

        private async Task<string> DownloadTextLimitedAsync(string url, int maximumBytes, CancellationToken token)
        {
            using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                using (Stream input = await response.Content.ReadAsStreamAsync())
                using (MemoryStream output = new MemoryStream())
                {
                    await CopyLimitedAsync(input, output, maximumBytes, token);
                    return Encoding.UTF8.GetString(output.ToArray());
                }
            }
        }

        private static async Task CopyLimitedAsync(Stream input, Stream output, long maximumBytes, CancellationToken token)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maximumBytes) throw new InvalidDataException("下载内容超过允许的大小。");
                await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
            }
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] bytes = hash.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(64);
                foreach (byte value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void EnsureHttps(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("更新地址不是安全的 HTTPS 地址。");
        }

        private static string PowerShellLiteral(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string QuoteCommandArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class GitHubRelease
        {
            public string tag_name { get; set; }
            public string name { get; set; }
            public string body { get; set; }
            public string html_url { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public GitHubAsset[] assets { get; set; }
        }

        private sealed class GitHubAsset
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
        }
    }
}
