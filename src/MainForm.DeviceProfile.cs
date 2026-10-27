using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using System.Windows.Forms;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private readonly SegmentedPicker siteModePicker = new SegmentedPicker();
        private string browserVersion;
        private string defaultUserAgent;
        private bool deviceProfileChanging;
        private bool mobileHomePending;
        private bool recoveringWebView;
        // 是否下发过手机仿真。电脑模式从未切过手机就完全不碰 CDP，保持与 1.10.1 一致的原生身份。
        private bool deviceOverridesApplied;
        private int lastProfileRepairGeneration = -1;

        private Task<string> CallCdpAsync(string method, Dictionary<string, object> arguments)
        {
            return webView.CoreWebView2.CallDevToolsProtocolMethodAsync(method,
                new JavaScriptSerializer().Serialize(arguments));
        }

        private async Task ApplyDeviceProfileAsync()
        {
            CoreWebView2 core = webView.CoreWebView2;
            if (CurrentSiteMode == SiteMode.Mobile)
            {
                string major = DeviceProfile.BrowserMajor(browserVersion).ToString(System.Globalization.CultureInfo.InvariantCulture);
                string ua = DeviceProfile.BuildMobileUserAgent(browserVersion);
                core.Settings.UserAgent = ua;
                await CallCdpAsync("Emulation.setUserAgentOverride", new Dictionary<string, object> {
                    { "userAgent", ua }, { "acceptLanguage", "zh-CN,zh;q=0.9" }, { "platform", "Linux armv8l" },
                    { "userAgentMetadata", new { brands = new[] { new { brand = "Chromium", version = major }, new { brand = "Google Chrome", version = major } },
                        fullVersionList = new[] { new { brand = "Chromium", version = major + ".0.0.0" }, new { brand = "Google Chrome", version = major + ".0.0.0" } },
                        fullVersion = major + ".0.0.0", platform = "Android", platformVersion = "14.0.0", architecture = "", model = "Pixel 8", mobile = true } } });
                await CallCdpAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { { "enabled", true }, { "maxTouchPoints", 5 } });
                // 真实推荐页已支持滚轮和上下键；鼠标转触摸没有切片收益，保留正常鼠标点击。
                await CallCdpAsync("Emulation.setEmitTouchEventsForMouse", new Dictionary<string, object> { { "enabled", false } });
                // 不开 mobile 视口：抖音主站在手机身份下仍给电脑布局，却带 initial-scale=0.5 的 meta viewport，
                // 开了会把整页按约 0.57 倍缩小渲染，字又小又糊。手机身份由 UA、Client Hints 和触摸信号表达即可。
                await CallCdpAsync("Emulation.clearDeviceMetricsOverride", new Dictionary<string, object>());
                deviceOverridesApplied = true;
            }
            else
            {
                if (!deviceOverridesApplied) return;
                core.Settings.UserAgent = defaultUserAgent;
                await CallCdpAsync("Emulation.clearDeviceMetricsOverride", new Dictionary<string, object>());
                await CallCdpAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { { "enabled", false } });
                await CallCdpAsync("Emulation.setEmitTouchEventsForMouse", new Dictionary<string, object> { { "enabled", false } });
                // 空 UA 等于撤销覆盖；带默认 UA 但不带 metadata 的覆盖会让 Chromium 停发 Sec-CH-UA 请求头。
                await CallCdpAsync("Emulation.setUserAgentOverride", new Dictionary<string, object> { { "userAgent", "" } });
                deviceOverridesApplied = false;
            }
        }

        private async Task ChangeSiteModeAsync(SiteMode mode)
        {
            if (deviceProfileChanging || CurrentSiteMode == mode || !webViewReady) return;
            string previous = settings.SiteMode;
            deviceProfileChanging = true;
            siteModePicker.Enabled = false;
            try
            {
                bool failed = false;
                try
                {
                    settings.SiteMode = mode.ToString();
                    await ApplyDeviceProfileAsync();
                    if (mode == SiteMode.Mobile) RestorePortraitBounds();
                    pageStyleFallback = false;
                    UpdateZoom();
                    mobileHomePending = mode == SiteMode.Mobile;
                    webView.CoreWebView2.Navigate(HomeUrl);
                    ScheduleSettingsSave();
                }
                catch (Exception exception)
                {
                    settings.SiteMode = previous;
                    Diagnostics.LogException("ChangeSiteMode", exception);
                    ShowTransientNotice("访问模式切换失败，请重新打开程序后重试。");
                    failed = true;
                }
                if (failed) { await ApplyDeviceProfileAsync(); UpdateZoom(); }
            }
            finally
            {
                deviceProfileChanging = false;
                UpdateSettingsUi();
            }
        }

        private async Task InspectDeviceProfileAsync()
        {
            if (!webViewReady || isQuitting || deviceProfileChanging) return;
            int generation = navigationGeneration;
            try
            {
                string snapshot = await webView.ExecuteScriptAsync(@"(() => ({ua:navigator.userAgent,
                  mobile:navigator.userAgentData?navigator.userAgentData.mobile:false,touch:navigator.maxTouchPoints,
                  coarse:matchMedia('(pointer:coarse)').matches,dpr:devicePixelRatio,width:innerWidth,
                  videos:Array.from(document.querySelectorAll('video')).map(v=>({w:v.videoWidth,h:v.videoHeight}))}))()");
                if (generation != navigationGeneration || isQuitting || deviceProfileChanging) return;
                Diagnostics.Log("device-profile mode=" + CurrentSiteMode + " zoom=" + webView.ZoomFactor.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + snapshot);
                Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(snapshot);
                bool mobile = CurrentSiteMode == SiteMode.Mobile;
                bool matches = data != null && Convert.ToBoolean(data["mobile"]) == mobile
                    && (Convert.ToInt32(data["touch"]) > 0) == mobile && Convert.ToBoolean(data["coarse"]) == mobile
                    && Convert.ToString(data["ua"]).Contains("Mobile") == mobile;
                // 每个完成事件最多校正一次，不重载页面，不让检查形成导航循环。
                if (!matches && lastProfileRepairGeneration != generation)
                {
                    lastProfileRepairGeneration = generation;
                    Diagnostics.Log("device-profile mismatch; reapply once");
                    await ApplyDeviceProfileAsync();
                }
                if (mobileHomePending && mobile && new Uri(webView.CoreWebView2.Source).AbsolutePath == "/jingxuan")
                {
                    string opened = await webView.ExecuteScriptAsync(PageScripts.OpenRecommendScript);
                    if (opened != "\"none\"" && opened != "null") mobileHomePending = false;
                }
            }
            catch (Exception exception) { Diagnostics.LogException("InspectDeviceProfile", exception); }
        }

        private async void OnWebProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (isQuitting || recoveringWebView) return;
            if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited
                && e.ProcessFailedKind != CoreWebView2ProcessFailedKind.BrowserProcessExited) return;
            recoveringWebView = true;
            Diagnostics.Log("web-process-failed " + e.ProcessFailedKind);
            try
            {
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                {
                    // 浏览器进程退出后旧 Core 已失效，必须重建控件再应用设备配置。
                    webViewReady = false;
                    VisibilityAwareWebView previous = webView;
                    contentHost.Controls.Remove(previous);
                    previous.Dispose();
                    webView = new VisibilityAwareWebView();
                    webView.Dock = DockStyle.Fill;
                    webView.DefaultBackgroundColor = Color.FromArgb(16, 16, 16);
                    contentHost.Controls.Add(webView);
                    webView.SendToBack();
                    webView.Visible = !settingsOpen;
                    await InitializeWebViewAsync();
                }
                else
                {
                    await ApplyDeviceProfileAsync();
                    webView.Reload();
                }
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("RecoverDeviceProfile", exception);
                navigationFailed = true;
                RefreshPageNotice();
            }
            finally { recoveringWebView = false; }
        }

        private async Task RunDeviceProfileRegressionAsync()
        {
            deviceProfileChanging = true;
            try
            {
                settings.SiteMode = "Mobile";
                await ApplyDeviceProfileAsync();
                AssertRegression(await PageIsTrueAsync("navigator.userAgent.includes('Mobile') && navigator.userAgentData.mobile && navigator.maxTouchPoints===5 && matchMedia('(pointer:coarse)').matches"), "mobile device profile signals agree");
                string mobileWidth = await webView.ExecuteScriptAsync("innerWidth");
                UpdateSettingsUi();
                AssertRegression(!leftNavToggle.Enabled && !topBarToggle.Enabled && !rightBarToggle.Enabled && !landscapeToggle.Enabled, "mobile desktop-only controls disabled");
                settings.SiteMode = "Desktop";
                await ApplyDeviceProfileAsync();
                AssertRegression(await PageIsTrueAsync("!navigator.userAgent.includes('Mobile') && !navigator.userAgentData.mobile && navigator.platform==='Win32' && navigator.maxTouchPoints===0 && !matchMedia('(pointer:coarse)').matches"), "desktop device profile restored");
                AssertRegression(await PageIsTrueAsync("navigator.userAgentData.brands.length>0 && navigator.userAgentData.platform==='Windows'"), "desktop client hints restored");
                // 手机身份不得改变布局视口：开 mobile 视口时无 meta 的页面会变成 980 宽，带 initial-scale=0.5 的抖音会被缩一半。
                AssertRegression(await PageIsTrueAsync("innerWidth===" + mobileWidth), "mobile profile leaves viewport unscaled");
            }
            finally { settings.SiteMode = "Desktop"; deviceProfileChanging = false; UpdateZoom(); UpdateSettingsUi(); }
        }
    }
}
