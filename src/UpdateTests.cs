using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniView.WebView2App
{
    // Offline transport fixtures: never contact GitHub or execute an installer.
    internal static class UpdateTests
    {
        internal static int Run(Func<bool, string, int> check)
        {
            int failures = 0;
            string root = Path.Combine(Path.GetTempPath(), "BoniuUpdateTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes("fixture executable bytes; never launched");
                string hash;
                using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "").ToLowerInvariant();
                UpdateInfo info = new UpdateInfo { Version = new Version(9, 0, 0), AssetUrl = "https://fixture.invalid/app", HashUrl = "https://fixture.invalid/hash" };
                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, hash, "ok")))
                {
                    PreparedUpdate prepared = service.DownloadAsync(info).GetAwaiter().GetResult();
                    failures += check(File.ReadAllText(prepared.ExecutablePath) == Encoding.UTF8.GetString(payload), "更新下载校验成功后才提供安装文件");
                }
                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, new string('0', 64), "ok")))
                    failures += check(Throws<InvalidDataException>(delegate { service.DownloadAsync(info).GetAwaiter().GetResult(); }), "更新哈希不匹配拒绝下载结果");
                failures += check(Directory.GetFiles(root, "*.part", SearchOption.AllDirectories).Length == 0, "哈希失败清理临时下载");

                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, hash, "interrupted")))
                    failures += check(Throws<IOException>(delegate { service.DownloadAsync(info).GetAwaiter().GetResult(); }), "下载正文中断可报告错误");
                failures += check(Directory.GetFiles(root, "*.part", SearchOption.AllDirectories).Length == 0, "正文中断清理临时下载");
                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, hash, "oversized")))
                    failures += check(Throws<InvalidDataException>(delegate { service.DownloadAsync(info).GetAwaiter().GetResult(); }), "超大更新文件拒绝下载");
                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, hash, "offline")))
                    failures += check(Throws<HttpRequestException>(delegate { service.CheckAsync().GetAwaiter().GetResult(); }), "断网更新检查可报告错误");
                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, hash, "timeout")))
                    failures += check(Throws<OperationCanceledException>(delegate { service.CheckAsync().GetAwaiter().GetResult(); }), "超时更新检查可报告错误");
                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, hash, "release")))
                {
                    UpdateInfo release = service.CheckAsync().GetAwaiter().GetResult();
                    failures += check(release != null && release.Version == new Version(99, 0, 0)
                        && release.AssetUrl.EndsWith(UpdateService.GetExecutableAssetName(IntPtr.Size)), "Release 选择当前架构资产");
                }
                info.AssetUrl = "http://fixture.invalid/app";
                using (UpdateService service = new UpdateService(root, new FixtureHandler(payload, hash, "offline")))
                    failures += check(Throws<InvalidDataException>(delegate { service.DownloadAsync(info).GetAwaiter().GetResult(); }), "更新拒绝非 HTTPS 地址");
                failures += check(UpdateService.ExtractSha256(new string('a', 65)) == null, "拒绝超过 64 位的伪哈希");
                failures += check(UpdateService.ExtractSha256("prefix " + hash) == null, "拒绝从任意文本中截取哈希");
                failures += check(FailureMessages.ForUpdate(new UnauthorizedAccessException()).Contains("权限"), "目录权限错误有可操作提示");
                failures += check(FailureMessages.ForUpdate(new TaskCanceledException()).Contains("超时"), "取消和超时提示");

                string install = UpdateService.BuildInstallScript(123, "C:\\fixture's\\old.exe", "C:\\fixture\\new.exe",
                    "C:\\fixture\\old.bak", "C:\\fixture\\healthy", "abc123", "C:\\fixture\\update.log", "C:\\fixture\\install.ps1");
                failures += check(install.Contains("fixture''s") && install.Contains("$backupReady"), "安装脚本引用转义与备份保护");
                failures += check(install.Contains("New version did not report healthy startup.")
                    && install.Contains("Copy-Item -LiteralPath $backup -Destination $target"), "安装脚本包含健康检查及回滚分支（静态检查）");
                failures += check(install.Contains("Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue"),
                    "健康启动后清理启动器备份");

                string retention = Path.Combine(root, "retention");
                Directory.CreateDirectory(retention);
                for (int i = 0; i < 5; i++)
                {
                    string candidate = Path.Combine(retention, "settings-before-update-" + i + ".json");
                    File.WriteAllText(candidate, i.ToString());
                    File.SetLastWriteTimeUtc(candidate, DateTime.UtcNow.AddMinutes(-i));
                }
                UpdateService.RetainNewestFiles(retention, "settings-before-update-*.json", 3);
                failures += check(Directory.GetFiles(retention).Length == 3
                    && File.Exists(Path.Combine(retention, "settings-before-update-0.json"))
                    && !File.Exists(Path.Combine(retention, "settings-before-update-4.json")), "设置备份只保留最近三份");

                string settingsPath = Path.Combine(root, "settings.json");
                SettingsStore store = new SettingsStore(settingsPath);
                store.Save(new AppSettings { Muted = true, ShowTrayIcon = true });
                failures += check(store.Load().Muted && store.Load().ShowTrayIcon, "设置首次写入和新字段读取");
                store.Save(new AppSettings { Muted = false });
                failures += check(!store.Load().Muted && Directory.GetFiles(root, "*.tmp").Length == 0, "设置原子替换后无残留");
                File.WriteAllText(settingsPath, "{broken");
                failures += check(store.Load().AutoHideEnabled, "损坏设置回退默认值");
            }
            catch (Exception exception)
            {
                failures += check(false, "离线测试异常: " + exception);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
            return failures;
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); return false; }
            catch (T) { return true; }
        }

        private sealed class FixtureHandler : HttpMessageHandler
        {
            private readonly byte[] payload;
            private readonly string hash;
            private readonly string mode;
            internal FixtureHandler(byte[] payload, string hash, string mode) { this.payload = payload; this.hash = hash; this.mode = mode; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (mode == "offline") throw new HttpRequestException("offline fixture");
                if (mode == "timeout") throw new TaskCanceledException("timeout fixture");
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
                if (mode == "release")
                {
                    string asset = UpdateService.GetExecutableAssetName(IntPtr.Size);
                    response.Content = new StringContent("{\"tag_name\":\"v99.0.0\",\"assets\":[{\"name\":\"" + asset
                        + "\",\"browser_download_url\":\"https://fixture.invalid/" + asset
                        + "\"},{\"name\":\"" + asset + ".sha256\",\"browser_download_url\":\"https://fixture.invalid/hash\"}]}");
                }
                else if (request.RequestUri.AbsolutePath == "/hash") response.Content = new StringContent(hash + "  app.exe");
                else if (mode == "interrupted") response.Content = new StreamContent(new InterruptedStream(payload));
                else
                {
                    response.Content = new ByteArrayContent(payload);
                    if (mode == "oversized") response.Content.Headers.ContentLength = 51L * 1024 * 1024;
                }
                return Task.FromResult(response);
            }
        }

        private sealed class InterruptedStream : MemoryStream
        {
            internal InterruptedStream(byte[] bytes) : base(bytes) { }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (Position > 0) throw new IOException("fixture connection dropped mid-body");
                return base.ReadAsync(buffer, offset, Math.Min(count, 4), cancellationToken);
            }
        }
    }
}
