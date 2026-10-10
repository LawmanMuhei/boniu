using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace MiniView.WebView2App
{
    internal static class Program
    {
        private const string MutexName = "Local\\MiniViewWebView2.SingleInstance";
        private const string ShowEventName = "Local\\MiniViewWebView2.ShowWindow";

        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            if (HasArgument(args, "--self-test")) return SelfTests.Run();
            if (HasArgument(args, "--update-check-test")) return RunUpdateCheckTest();

            bool testMode = HasArgument(args, "--smoke-test") || HasArgument(args, "--live-smoke-test")
                || HasArgument(args, "--settings-smoke-test") || HasArgument(args, "--dom-probe") || HasArgument(args, "--regression-test");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Diagnostics.Start(testMode ? Path.Combine(Path.GetTempPath(), "MiniViewWebView2Smoke", System.Diagnostics.Process.GetCurrentProcess().Id.ToString(), "logs") : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniViewWebView2", "logs"));
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Diagnostics.LogException("ThreadException", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Diagnostics.LogException("UnhandledException", e.ExceptionObject as Exception);
            };
            Diagnostics.Log("args=" + string.Join(" ", args));

            if (!testMode)
            {
                bool createdNew;
                using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
                {
                    if (!createdNew)
                    {
                        try
                        {
                            using (EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName)) existing.Set();
                        }
                        catch (Exception exception)
                        {
                            Diagnostics.LogException("NotifyExistingInstance", exception);
                        }
                        return 0;
                    }
                    return RunForm(args);
                }
            }
            return RunForm(args);
        }

        private static int RunUpdateCheckTest()
        {
            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "MiniViewWebView2UpdateCheck");
                UpdateInfo info = new UpdateService(folder).CheckAsync().GetAwaiter().GetResult();
                Console.WriteLine(info == null ? "update endpoint ok; no newer release"
                    : "update endpoint ok; release=" + info.Version.ToString(3));
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("update endpoint failed: " + exception.Message);
                return 5;
            }
        }

        private static int RunForm(string[] args)
        {
            bool smokeTest = HasArgument(args, "--smoke-test");
            bool liveSmokeTest = HasArgument(args, "--live-smoke-test");
            bool settingsSmokeTest = HasArgument(args, "--settings-smoke-test");
            bool domProbe = HasArgument(args, "--dom-probe");
            string healthToken = GetArgumentValue(args, "--update-health-token");

            if (smokeTest || liveSmokeTest || settingsSmokeTest || domProbe || HasArgument(args, "--regression-test"))
            {
                using (MainForm testForm = new MainForm(smokeTest, liveSmokeTest, settingsSmokeTest, domProbe, null, HasArgument(args, "--regression-test")))
                    Application.Run(testForm);
                return Environment.ExitCode;
            }

            using (EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            using (MainForm form = new MainForm(smokeTest, liveSmokeTest, settingsSmokeTest, domProbe, healthToken))
            {
                RegisteredWaitHandle waiter = ThreadPool.RegisterWaitForSingleObject(showEvent,
                    delegate { form.ShowFromSecondInstance(); }, null, Timeout.Infinite, false);
                Application.Run(form);
                waiter.Unregister(null);
            }
            return Environment.ExitCode;
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string argument in args)
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string GetArgumentValue(string[] args, string expected)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }

    internal static class SelfTests
    {
        internal static int Run()
        {
            int failures = 0;
            failures += Check(WindowRules.IsDouyinUrl("https://www.douyin.com/?recommend=1"), "允许抖音 HTTPS");
            failures += Check(WindowRules.IsDouyinUrl("https://live.douyin.com/123"), "允许抖音子域名");
            failures += Check(!WindowRules.IsDouyinUrl("http://www.douyin.com/"), "拒绝非 HTTPS");
            failures += Check(!WindowRules.IsDouyinUrl("https://douyin.com.example.com/"), "拒绝伪造域名");
            failures += Check(WindowRules.IsLiveUrl("https://live.douyin.com/123"), "识别直播域名");
            failures += Check(WindowRules.IsLiveUrl("https://www.douyin.com/live/123"), "识别直播路径");
            failures += Check(Math.Abs(WindowRules.CalculateZoomFactor(280) - 0.44) < 0.001, "最小缩放");
            failures += Check(Math.Abs(WindowRules.CalculateZoomFactor(726) - 1.0) < 0.001, "正常缩放");
            // 清爽模式顶部工具栏迟滞：贴顶 6px 内唤出，离开工具栏区域 44px 才收起，
            // 上甩越过窗口上边缘也算贴顶（10ms 采样会跳过窄带）。
            const int reveal = 6, keep = 44;
            failures += Check(!WindowRules.ShouldRevealToolbar(200, 30, 420, false, reveal, keep), "顶部悬停：未唤出时中段不触发");
            failures += Check(WindowRules.ShouldRevealToolbar(200, 6, 420, false, reveal, keep), "顶部悬停：贴顶触发带唤出");
            failures += Check(WindowRules.ShouldRevealToolbar(200, 0, 420, false, reveal, keep), "顶部悬停：顶边唤出");
            failures += Check(WindowRules.ShouldRevealToolbar(200, -20, 420, false, reveal, keep), "顶部悬停：上甩越过上边缘仍唤出");
            failures += Check(!WindowRules.ShouldRevealToolbar(200, -60, 420, false, reveal, keep), "顶部悬停：离窗口上方过远不唤出");
            failures += Check(WindowRules.ShouldRevealToolbar(200, 40, 420, true, reveal, keep), "顶部悬停：已唤出时在工具栏区域保持");
            failures += Check(!WindowRules.ShouldRevealToolbar(200, 45, 420, true, reveal, keep), "顶部悬停：离开工具栏区域后收起");
            failures += Check(!WindowRules.ShouldRevealToolbar(-5, 3, 420, false, reveal, keep)
                && !WindowRules.ShouldRevealToolbar(430, 3, 420, true, reveal, keep), "顶部悬停：横向超出窗口不唤出也不保持");
            // 悬停唤出的工具栏走浮层（不占布局高度），其余情况仍占位。
            failures += Check(WindowRules.ShouldOverlayToolbar(true, false, false, true), "顶部悬停：清爽模式悬停唤出用浮层");
            failures += Check(!WindowRules.ShouldOverlayToolbar(true, false, false, false), "顶部悬停：未唤出时不浮层");
            failures += Check(!WindowRules.ShouldOverlayToolbar(false, false, false, true), "顶部悬停：正常模式工具栏仍占位");
            failures += Check(!WindowRules.ShouldOverlayToolbar(true, true, false, true), "顶部悬停：彻底隐藏工具栏时不浮层");
            failures += Check(!WindowRules.ShouldOverlayToolbar(true, false, true, true), "顶部悬停：设置页打开时工具栏仍占位");
            failures += Check(WindowRules.NormalizeAutoHideDelay(100) == 100, "支持 0.1 秒隐藏延迟");
            failures += Check(WindowRules.NormalizeAutoHideDelay(300) == 300, "支持 0.3 秒隐藏延迟");
            failures += Check(WindowRules.NormalizeAutoHideDelay(500) == 500, "支持 0.5 秒隐藏延迟");
            failures += Check(WindowRules.NormalizeAutoHideDelay(0) == 100, "无效隐藏延迟回退默认值");

            AppSettings defaultSettings = new AppSettings();
            failures += Check(defaultSettings.AutoHideEnabled, "默认开启鼠标移出隐藏");
            failures += Check(defaultSettings.AutoHideDelayMilliseconds == 100, "默认隐藏延迟为 0.1 秒");
            failures += Check(!defaultSettings.HideWhenInactive, "默认关闭失焦隐藏");
            HotkeyDefinition immersiveDefault;
            failures += Check(HotkeyDefinition.TryParse(defaultSettings.ImmersiveShortcut, out immersiveDefault)
                && immersiveDefault.Serialize() == "Control+Alt+F", "清爽模式默认快捷键为 Ctrl+Alt+F");
            System.Web.Script.Serialization.JavaScriptSerializer serializer =
                new System.Web.Script.Serialization.JavaScriptSerializer();
            AppSettings upgradedSettings = serializer.Deserialize<AppSettings>("{\"Muted\":true}");
            failures += Check(upgradedSettings.AutoHideEnabled, "旧设置升级后保持鼠标移出隐藏");
            failures += Check(upgradedSettings.AutoHideDelayMilliseconds == 100, "旧设置升级后使用 0.1 秒延迟");

            Rectangle landscape = WindowRules.CalculateLandscapeBounds(new Rectangle(100, 100, 420, 760), new Rectangle(0, 0, 1920, 1040));
            failures += Check(landscape.Width == 760 && landscape.Height == 428, "直播横屏尺寸");
            failures += Check(landscape.Left >= 0 && landscape.Top >= 0, "直播窗口保持在屏幕内");

            HotkeyDefinition first;
            HotkeyDefinition second;
            failures += Check(HotkeyDefinition.TryParse("Control+Alt+D", out first), "解析隐藏快捷键");
            failures += Check(HotkeyDefinition.TryParse("Alt+Control+D", out second) && first.ConflictsWith(second), "快捷键冲突识别");
            failures += Check(!HotkeyDefinition.TryParse("D", out second), "拒绝无修饰键快捷键");
            failures += Check(!HotkeyDefinition.TryParse("Alt+F4", out second), "拒绝系统快捷键");
            failures += Check(immersiveDefault != null && !immersiveDefault.ConflictsWith(first),
                "清爽模式默认快捷键不与隐藏快捷键冲突");
            HotkeyDefinition miniDefault;
            failures += Check(HotkeyDefinition.TryParse(defaultSettings.MiniShortcut, out miniDefault)
                && miniDefault.Serialize() == "Control+Alt+S", "迷你小窗默认快捷键为 Ctrl+Alt+S");
            failures += Check(miniDefault != null && !miniDefault.ConflictsWith(first)
                && !miniDefault.ConflictsWith(immersiveDefault), "迷你小窗默认快捷键不与其它快捷键冲突");
            Rectangle corner = WindowRules.CalculateCornerBounds(
                new Rectangle(0, 0, 1920, 1040), WindowRules.MiniSize, 16);
            failures += Check(corner.Right == 1920 - 16 && corner.Bottom == 1040 - 16
                && corner.Width == WindowRules.MiniSize.Width, "迷你小窗默认停靠屏幕右下角");
            failures += Check(upgradedSettings.MiniShortcut == "Control+Alt+S" && !upgradedSettings.MiniMode,
                "旧设置升级后保持迷你小窗默认值");

            Version parsedVersion;
            failures += Check(UpdateService.TryParseVersion("v1.7.0", out parsedVersion)
                && parsedVersion == new Version(1, 7, 0), "解析 GitHub v 版本标签");
            failures += Check(UpdateService.TryParseVersion("1.8", out parsedVersion)
                && parsedVersion == new Version(1, 8, 0), "补齐两段版本号");
            failures += Check(!UpdateService.TryParseVersion("latest", out parsedVersion), "拒绝无效版本标签");
            failures += Check(UpdateService.ExtractSha256("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789  波妞摸鱼.exe")
                == "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", "解析 SHA-256 校验文件");
            failures += Check(UpdateService.GetExecutableAssetName(8) == "BoniuMoyu.exe", "64 位程序选择 x64 更新资产");
            failures += Check(UpdateService.GetExecutableAssetName(4) == "BoniuMoyu-x86.exe", "32 位程序选择 x86 更新资产");
            failures += Check(UpdateService.IsTrustedDownloadHost("github.com")
                && UpdateService.IsTrustedDownloadHost("GITHUB.COM")
                && UpdateService.IsTrustedDownloadHost("objects.githubusercontent.com"), "更新下载只接受 GitHub 主机");
            failures += Check(!UpdateService.IsTrustedDownloadHost("evilgithubusercontent.com")
                && !UpdateService.IsTrustedDownloadHost("github.com.evil.example")
                && !UpdateService.IsTrustedDownloadHost(""), "拒绝伪装成 GitHub 的主机");

            using (System.Drawing.Drawing2D.GraphicsPath degenerate =
                UiPaint.RoundedPath(new RectangleF(0.5F, 0.5F, -4F, -3F), 8F))
                failures += Check(degenerate.PointCount == 0, "退化尺寸不生成可填充路径");
            using (System.Drawing.Drawing2D.GraphicsPath normal =
                UiPaint.RoundedPath(new RectangleF(0F, 0F, 30F, 28F), 8F))
                failures += Check(normal.PointCount > 0, "正常尺寸生成圆角路径");

            WindowLifecycle state = new WindowLifecycle();
            long hidden = state.SetVisible(false);
            failures += Check(!state.Visible && state.IsCurrent(hidden), "隐藏状态有效");
            long shown = state.SetVisible(true);
            failures += Check(state.Visible && state.IsCurrent(shown) && !state.IsCurrent(hidden), "恢复使旧隐藏回调过期");
            long hiddenAgain = state.SetVisible(false);
            failures += Check(!state.IsCurrent(shown) && state.IsCurrent(hiddenAgain), "再隐藏使旧恢复回调过期");
            long refreshed = state.Refresh();
            failures += Check(!state.IsCurrent(hiddenAgain) && state.IsCurrent(refreshed), "导航或媒体刷新使旧操作过期");
            state.Close();
            failures += Check(!state.IsCurrent(refreshed), "关闭后回调不能修改界面");
            failures += Check(!defaultSettings.ShowTrayIcon, "默认不强制显示托盘");
            failures += Check(!upgradedSettings.ShowTrayIcon, "旧设置可兼容新增托盘选项");
            failures += Check(PageScripts.LiveProbeScript.Contains("style.remove()"), "退出直播清除适配样式");
            failures += Check(MediaScripts.Build(42, false).Contains("revision = 42"), "媒体命令包含操作代次");
            failures += Check(MediaScripts.Build(42, true).Contains("revision < state.revision"), "媒体命令拒绝过期请求");
            failures += Check(MiniView.CommandLineArguments.QuoteWindowsArgument("") == "\"\"", "空参数保留为空字符串");
            failures += Check(MiniView.CommandLineArguments.QuoteWindowsArgument("plain") == "plain", "简单参数无需引号");
            failures += Check(MiniView.CommandLineArguments.QuoteWindowsArgument("C:\\Program Files\\Boniu\\")
                == "\"C:\\Program Files\\Boniu\\\\\"", "带空格和尾反斜杠的参数正确引用");
            failures += Check(MiniView.CommandLineArguments.QuoteWindowsArgument("say\"hi")
                == "\"say\\\"hi\"", "参数内引号正确转义");

            failures += UpdateTests.Run(Check);
            failures += Check(UpdateService.CurrentVersion.ToString(3) == AppVersion.Current, "程序集版本与单一版本源一致");
            Console.WriteLine(failures == 0 ? checkedCount + " tests passed" : failures + " tests failed");
            return failures == 0 ? 0 : 1;
        }

        private static int checkedCount;

        private static int Check(bool result, string name)
        {
            checkedCount++;
            if (result) return 0;
            Console.Error.WriteLine("FAILED: " + name);
            return 1;
        }
    }
}
