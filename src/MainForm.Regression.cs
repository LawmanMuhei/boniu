using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private readonly List<object> regressionResults = new List<object>();
        private readonly List<object> performanceSamples = new List<object>();

        private async Task RunRegressionAsync()
        {
            try
            {
                webView.NavigateToString(RegressionHtml);
                await WaitForPageAsync("window.fixtureReady === true");
                AssertRegression(!settings.AutoLandscapeLive && !settings.ShowTrayIcon, "isolated default settings");
                AssertRegression(await PageIsTrueAsync("!document.getElementById('playing').paused && document.getElementById('manual').paused"), "synthetic video playing; manually paused video preserved");
                await MeasurePerformanceAsync("synthetic-playing");

                double maxHideMs = 0;
                for (int i = 0; i < 20; i++)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    HideWindow();
                    maxHideMs = Math.Max(maxHideMs, timer.Elapsed.TotalMilliseconds);
                    AssertRegression(!Visible && webView.CoreWebView2.IsMuted, "hidden-and-muted-" + i);
                    // No delay: deliberately leave pause scripts in flight when restore is requested.
                    ShowWindow();
                }
                await WaitForPageAsync("!document.getElementById('playing').paused && document.getElementById('manual').paused");
                AssertRegression(Visible && !webView.CoreWebView2.IsMuted, "rapid-toggle final state visible and unmuted");
                performanceSamples.Add(new { phase = "rapid-toggle", maxHideDispatchMs = maxHideMs, iterations = 20 });

                // A blocked renderer must not prevent the native window from becoming hidden.
                Task<string> busyRenderer = webView.ExecuteScriptAsync("(() => { const until=performance.now()+700; while(performance.now()<until){}; return true; })()");
                Stopwatch hideTimer = Stopwatch.StartNew();
                HideWindow();
                double busyHideMs = hideTimer.Elapsed.TotalMilliseconds;
                AssertRegression(!Visible, "window hides while renderer has queued busy work");
                await busyRenderer;
                await WaitForPageAsync("document.getElementById('playing').paused");
                performanceSamples.Add(new { phase = "busy-renderer-hide", hideDispatchMs = busyHideMs });
                await MeasurePerformanceAsync("hidden-before-suspend");
                await SuspendWebViewIfHiddenAsync();
                AssertRegression(webViewSuspended || suspendTimer.Enabled, "suspend succeeded or bounded retry scheduled");
                if (webViewSuspended) await MeasurePerformanceAsync("suspended");
                Stopwatch restore = Stopwatch.StartNew();
                ShowWindow();
                await WaitForPageAsync("!document.getElementById('playing').paused");
                performanceSamples.Add(new { phase = "restore-playback", elapsedMs = restore.Elapsed.TotalMilliseconds });
                AssertRegression(!webViewSuspended && Visible, "suspended view restored");
                AssertRegression(await WebViewRendersFixtureAsync(), "restored WebView renders fixture frames on screen");

                HideWindow();
                Task pendingSuspend = SuspendWebViewIfHiddenAsync();
                ShowWindow();
                await pendingSuspend;
                await WaitForPageAsync("!document.getElementById('playing').paused");
                AssertRegression(Visible && !webViewSuspended, "show while suspend request pending");

                // Test out-of-order DOM media commands without disturbing the host operation stamp.
                await webView.ExecuteScriptAsync(MediaScripts.Build(900000, false));
                await webView.ExecuteScriptAsync(MediaScripts.Build(899999, true));
                AssertRegression(await PageIsTrueAsync("document.getElementById('playing').paused"), "stale resume command rejected");
                await webView.ExecuteScriptAsync(MediaScripts.Build(900001, true));
                await WaitForPageAsync("!document.getElementById('playing').paused");
                await webView.ExecuteScriptAsync("delete window.__boniuMediaState");

                settings.ImmersiveMode = true;
                ApplyPageElementVisibility();
                await WaitForPageAsync("getComputedStyle(document.getElementById('douyin-header')).display === 'none'");
                AssertRegression(await PageIsTrueAsync("getComputedStyle(document.getElementById('playing')).position !== 'fixed'"), "native player positioning preserved");
                for (int i = 0; i < 6; i++)
                {
                    ToggleSettings();
                    AssertRegression(settingsOpen && !webView.Visible, "settings-open-" + i);
                    NavigateBack();
                    AssertRegression(!settingsOpen && webView.Visible, "settings-back-" + i);
                    await webView.ExecuteScriptAsync("window.nextFixtureVideo()");
                    await WaitForPageAsync("!document.getElementById('playing').paused");
                }
                AssertRegression(!pageStyleFallback, "healthy video switching does not trigger fallback");
                await webView.ExecuteScriptAsync("document.getElementById('playing').style.width='10px'");
                await WaitForConditionAsync(delegate { return pageStyleFallback; }, "layout fallback message", 7000);
                AssertRegression(pageNotice.Visible, "fallback notice visible outside settings");
                UpdateSettingsUi();
                AssertRegression(immersiveStatus.Text.Contains("回退"), "fallback status survives settings refresh");
                await webView.ExecuteScriptAsync("document.getElementById('playing').style.width='100%'");
                ReapplyPageStyles();
                await WaitForPageAsync("getComputedStyle(document.getElementById('douyin-header')).display === 'none'");
                AssertRegression(!pageStyleFallback && !pageNotice.Visible, "manual style retry clears fallback");

                settings.ImmersiveMode = false;
                settings.AutoLandscapeLive = true;
                Rectangle portrait = Bounds;
                webView.NavigateToString(LiveSmokeTestHtml);
                await WaitForPageAsync("document.getElementById('PlayerLayout') !== null");
                await WaitForConditionAsync(delegate { return liveLandscapeApplied; }, "live landscape", 7000);
                AssertRegression(Width > Height, "live landscape bounds");
                await WaitForPageAsync("getComputedStyle(document.getElementById('HeaderLayout')).display === 'none'");
                await webView.ExecuteScriptAsync("document.querySelector('.__livingPlayer__').className='finished'");
                await WaitForConditionAsync(delegate { return !liveLandscapeApplied; }, "leave live", 7000);
                AssertRegression(Bounds == portrait, "portrait bounds restored after live exit");
                AssertRegression(await PageIsTrueAsync("!document.getElementById('__boniuLiveStyle') && getComputedStyle(document.getElementById('HeaderLayout')).display !== 'none'"), "live CSS removed on exit");

                navigationFailed = true;
                RefreshPageNotice();
                AssertRegression(pageNotice.Visible && pageNotice.Text.Contains("重试"), "navigation error has retry action");
                navigationFailed = false;
                RefreshPageNotice();
                settings.AutoLandscapeLive = false;
                webView.NavigateToString(RegressionHtml);
                await WaitForPageAsync("window.fixtureReady === true");
                await MeasurePerformanceAsync("after-settings-and-live-cycles");
                // An active MediaStream may make WebView2 decline suspension. Also probe an
                // idle document so the report distinguishes platform refusal from a passed suspend.
                webView.NavigateToString("<!doctype html><html><body id='idle-fixture'>idle suspend fixture</body></html>");
                await WaitForPageAsync("document.getElementById('idle-fixture') !== null");
                HideWindow();
                await Task.Delay(300);
                await SuspendWebViewIfHiddenAsync();
                performanceSamples.Add(new { phase = "idle-suspend-result", suspended = webViewSuspended });
                if (webViewSuspended) await MeasurePerformanceAsync("idle-suspended");
                else Console.WriteLine("NOTE: WebView2 declined idle suspension; retry path verified, suspended performance unavailable.");
                ShowWindow();
                await WaitForPageAsync("document.getElementById('idle-fixture') !== null");
                AssertRegression(Visible && !webViewSuspended, "idle document restored after suspend attempt");
                webView.NavigateToString(RegressionHtml);
                await WaitForPageAsync("window.fixtureReady === true");
                AssertRegression(await WebViewRendersFixtureAsync(), "WebView renders after idle suspend cycle");
                AssertRegression(!File.Exists(Path.Combine(appFolder, "settings.json")), "test has not persisted settings");
                Console.WriteLine("Regression: " + regressionResults.Count + " checks passed");
                Environment.ExitCode = 0;
            }
            catch (Exception exception)
            {
                Environment.ExitCode = 6;
                regressionResults.Add(new { name = "exception", passed = false, detail = exception.ToString() });
                Console.Error.WriteLine("Regression failed: " + exception);
            }
            finally
            {
                try
                {
                    Directory.CreateDirectory(appFolder);
                    File.WriteAllText(Path.Combine(appFolder, "regression.json"), new JavaScriptSerializer().Serialize(new
                    {
                        scope = "Isolated synthetic WebView2 fixture; not real Douyin/network/decoded-stream benchmark",
                        architecture = IntPtr.Size == 4 ? "x86" : "x64",
                        checks = regressionResults,
                        performance = performanceSamples
                    }));
                }
                catch (Exception exception) { Console.Error.WriteLine("Report failed: " + exception.Message); Environment.ExitCode = 6; }
                QuitApplication();
            }
        }

        private void AssertRegression(bool passed, string name)
        {
            regressionResults.Add(new { name = name, passed = passed });
            if (!passed) throw new InvalidOperationException(name);
        }

        private async Task<bool> PageIsTrueAsync(string expression)
        {
            return string.Equals(await webView.ExecuteScriptAsync("Boolean(" + expression + ")"), "true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task WaitForPageAsync(string expression)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 10000)
            {
                if (await PageIsTrueAsync(expression)) return;
                await Task.Delay(80);
            }
            throw new TimeoutException("Page condition: " + expression);
        }

        private async Task WaitForConditionAsync(Func<bool> condition, string name, int milliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < milliseconds)
            {
                if (condition()) return;
                await Task.Delay(80);
            }
            throw new TimeoutException(name);
        }

        private async Task<bool> WebViewRendersFixtureAsync()
        {
            // Screen sampling proves the controller is visible and compositing after restore;
            // ExecuteScript alone succeeds even on an invisible or black WebView.
            for (int attempt = 0; attempt < 15; attempt++)
            {
                Color color = SampleWebViewCenterColor();
                if (NearColor(color, Color.FromArgb(22, 125, 76)) || NearColor(color, Color.FromArgb(34, 102, 170))) return true;
                await Task.Delay(100);
            }
            Console.Error.WriteLine("WebView center pixel: " + SampleWebViewCenterColor());
            return false;
        }

        private Color SampleWebViewCenterColor()
        {
            try
            {
                Rectangle screen = webView.RectangleToScreen(webView.ClientRectangle);
                if (screen.Width <= 0 || screen.Height <= 0) return Color.Empty;
                using (Bitmap bitmap = new Bitmap(1, 1))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2), Point.Empty, new Size(1, 1));
                    return bitmap.GetPixel(0, 0);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Screen sample failed: " + exception.Message);
                return Color.Empty;
            }
        }

        private static bool NearColor(Color actual, Color expected)
        {
            return Math.Abs(actual.R - expected.R) <= 40 && Math.Abs(actual.G - expected.G) <= 40 && Math.Abs(actual.B - expected.B) <= 40;
        }

        private sealed class ProcessSample
        {
            internal double CpuMs;
            internal long WorkingSet;
            internal long PrivateBytes;
        }

        private Dictionary<int, ProcessSample> ReadPerformanceProcesses()
        {
            HashSet<int> ids = new HashSet<int>();
            ids.Add(Process.GetCurrentProcess().Id);
            foreach (var info in webView.CoreWebView2.Environment.GetProcessInfos()) ids.Add(info.ProcessId);
            Dictionary<int, ProcessSample> result = new Dictionary<int, ProcessSample>();
            foreach (int id in ids)
            {
                try
                {
                    using (Process process = Process.GetProcessById(id))
                        result[id] = new ProcessSample { CpuMs = process.TotalProcessorTime.TotalMilliseconds,
                            WorkingSet = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64 };
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            return result;
        }

        private async Task MeasurePerformanceAsync(string phase)
        {
            Dictionary<int, ProcessSample> before = ReadPerformanceProcesses();
            Stopwatch timer = Stopwatch.StartNew();
            await Task.Delay(1200);
            Dictionary<int, ProcessSample> after = ReadPerformanceProcesses();
            double elapsedMs = timer.Elapsed.TotalMilliseconds;
            double cpuMs = 0;
            foreach (KeyValuePair<int, ProcessSample> entry in after)
                if (before.ContainsKey(entry.Key)) cpuMs += Math.Max(0, entry.Value.CpuMs - before[entry.Key].CpuMs);
            performanceSamples.Add(new
            {
                phase = phase, elapsedMs = elapsedMs,
                cpuMachinePercent = 100 * cpuMs / elapsedMs / Environment.ProcessorCount,
                workingSetMiB = after.Values.Sum(x => x.WorkingSet) / 1048576.0,
                privateMiB = after.Values.Sum(x => x.PrivateBytes) / 1048576.0,
                processes = after.Count,
                note = "Host + isolated WebView processes; working sets may double-count shared pages; CPU only counts processes present in both samples"
            });
        }

        private const string RegressionHtml = @"<!doctype html><html id=""dark""><head><style>
            html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#111;color:#fff}
            [data-e2e='feed-active-video']{width:100%;height:100%}video#playing{width:100%;height:100%}
            #douyin-header{position:absolute;top:0}#manual{display:none}
            </style></head><body><header id=""douyin-header"">fixture header</header>
            <div data-e2e=""feed-active-video""><video id=""playing"" muted playsinline></video></div>
            <video id=""manual"" muted></video><script>
            const canvas=document.createElement('canvas');canvas.width=320;canvas.height=180;
            const ctx=canvas.getContext('2d');let frame=0;
            setInterval(()=>{ctx.fillStyle=frame++%2?'#167d4c':'#2266aa';ctx.fillRect(0,0,320,180);},100);
            const stream=canvas.captureStream(10);
            const video=document.getElementById('playing');video.srcObject=stream;
            document.getElementById('manual').srcObject=stream;
            video.play().then(()=>window.fixtureReady=true);
            window.nextFixtureVideo=async()=>{const old=document.getElementById('playing');old.pause();
                const next=old.cloneNode();next.srcObject=stream;old.replaceWith(next);await next.play();};
            </script></body></html>";
    }
}
