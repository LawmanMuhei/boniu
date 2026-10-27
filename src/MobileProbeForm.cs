using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniView.WebView2App
{
    // 仅 --mobile-probe 启用：先确认 H5 是否可用，再决定正式模式的入口与默认值。
    internal sealed class MobileProbeForm : Form
    {
        private readonly WebView2 view = new WebView2();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<object> events = new List<object>();
        private readonly List<object> pages = new List<object>();
        private readonly string output;
        private readonly string profile;
        private TaskCompletionSource<bool> navigation;

        internal MobileProbeForm(string outputFolder)
        {
            output = Path.GetFullPath(string.IsNullOrEmpty(outputFolder)
                ? Path.Combine(Path.GetTempPath(), "BoniuMobileProbe", DateTime.Now.ToString("yyyyMMdd-HHmmss")) : outputFolder);
            profile = Path.Combine(Path.GetTempPath(), "BoniuMobileProbeProfile", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            Diagnostics.Start(Path.Combine(output, "logs"));
            Text = "波妞摸鱼 · 手机网页可行性探针（独立登录）";
            ClientSize = new Size(420, 724);
            StartPosition = FormStartPosition.CenterScreen;
            view.Dock = DockStyle.Fill;
            Controls.Add(view);
            Shown += async delegate { await RunAsync(); };
            FormClosed += delegate { view.Dispose(); };
        }

        private Task<string> Cdp(string method, Dictionary<string, object> arguments)
        {
            return view.CoreWebView2.CallDevToolsProtocolMethodAsync(method, json.Serialize(arguments));
        }

        private void Record(string kind, string url, object detail)
        {
            events.Add(new { time = DateTime.UtcNow.ToString("o"), kind = kind, url = url, detail = detail });
            Diagnostics.Log(kind + ": " + url + " " + json.Serialize(detail));
        }

        private async Task RunAsync()
        {
            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profile,
                    new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
                await view.EnsureCoreWebView2Async(environment);
                CoreWebView2 core = view.CoreWebView2;
                core.IsMuted = true;
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e)
                { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
                core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    e.Cancel = !WindowRules.IsDouyinUrl(e.Uri);
                    Record(e.Cancel ? "navigation blocked" : "navigation", e.Uri,
                        new { redirected = e.IsRedirected, navigationId = e.NavigationId });
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    Record("navigation completed", core.Source, new { success = e.IsSuccess, error = e.WebErrorStatus.ToString() });
                    if (navigation != null) navigation.TrySetResult(e.IsSuccess);
                };
                core.LaunchingExternalUriScheme += delegate(object sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
                { e.Cancel = true; Record("external scheme blocked", e.Uri, null); };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
                { e.Handled = true; Record("new window blocked", e.Uri, null); };
                core.SourceChanged += delegate { Record("source changed", core.Source, null); };
                core.WebResourceResponseReceived += delegate(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
                {
                    if (e.Request.Uri == "https://m.douyin.com/" || e.Request.Uri == "https://www.douyin.com/"
                        || e.Request.Uri == "https://www.douyin.com/?recommend=1")
                        Record("entry response", e.Request.Uri, new { status = e.Response.StatusCode });
                };

                // 同一匿名会话、相同窗口，先留桌面缩放 0.58 与 1.0 的对照。
                view.ZoomFactor = 0.58;
                await NavigateAsync("https://www.douyin.com/?recommend=1");
                await CaptureAsync("desktop-058");
                view.ZoomFactor = 1.0;
                await Task.Delay(2000);
                await CaptureAsync("desktop-100");
                Record("open recommend", core.Source, await view.ExecuteScriptAsync(PageScripts.OpenRecommendScript));
                await Task.Delay(6000);
                await CaptureAsync("desktop-recommend");
                await ProbeFeedAsync("desktop");

                string major = DeviceProfile.BrowserMajor(environment.BrowserVersionString).ToString(System.Globalization.CultureInfo.InvariantCulture);
                string ua = DeviceProfile.BuildMobileUserAgent(environment.BrowserVersionString);
                core.Settings.UserAgent = ua;
                object[] brands = { new { brand = "Chromium", version = major }, new { brand = "Google Chrome", version = major } };
                object[] versions = { new { brand = "Chromium", version = major + ".0.0.0" }, new { brand = "Google Chrome", version = major + ".0.0.0" } };
                await Cdp("Emulation.setUserAgentOverride", new Dictionary<string, object> {
                    { "userAgent", ua }, { "acceptLanguage", "zh-CN,zh;q=0.9" }, { "platform", "Linux armv8l" },
                    { "userAgentMetadata", new { brands = brands, fullVersionList = versions, fullVersion = major + ".0.0.0",
                        platform = "Android", platformVersion = "14.0.0", architecture = "", model = "Pixel 8", mobile = true } } });
                await Cdp("Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { { "enabled", true }, { "maxTouchPoints", 5 } });
                await Cdp("Emulation.setEmitTouchEventsForMouse", new Dictionary<string, object> { { "enabled", true }, { "configuration", "mobile" } });
                await Cdp("Emulation.setDeviceMetricsOverride", new Dictionary<string, object> {
                    { "width", 0 }, { "height", 0 }, { "deviceScaleFactor", 0 }, { "mobile", true } });

                string[] urls = { "https://www.douyin.com/", "https://www.douyin.com/?recommend=1", "https://m.douyin.com/" };
                for (int i = 0; i < urls.Length; i++)
                {
                    await NavigateAsync(urls[i]);
                    await CaptureAsync("mobile-" + (i + 1));
                    await Cdp("Input.synthesizeScrollGesture", new Dictionary<string, object> {
                        { "x", view.ClientSize.Width / 2 }, { "y", view.ClientSize.Height / 2 },
                        { "yDistance", -view.ClientSize.Height * 0.6 }, { "gestureSourceType", "touch" }, { "speed", 1200 } });
                    await Task.Delay(2500);
                    await CaptureAsync("mobile-" + (i + 1) + "-after-swipe");
                    if (i == 1)
                    {
                        Record("open recommend", core.Source, await view.ExecuteScriptAsync(PageScripts.OpenRecommendScript));
                        await Task.Delay(6000);
                        await CaptureAsync("mobile-recommend");
                        await Cdp("Input.synthesizeScrollGesture", new Dictionary<string, object> {
                            { "x", view.ClientSize.Width / 2 }, { "y", view.ClientSize.Height / 2 },
                            { "yDistance", -view.ClientSize.Height * 0.6 }, { "gestureSourceType", "touch" }, { "speed", 1200 } });
                        await Task.Delay(2500);
                        await CaptureAsync("mobile-recommend-after-swipe");
                        await ProbeFeedAsync("mobile");
                        ClientSize = new Size(360, 640);
                        await Task.Delay(1500);
                        await CaptureAsync("mobile-resized");
                        ClientSize = new Size(420, 724);
                    }
                }
                File.WriteAllText(Path.Combine(output, "result.json"), json.Serialize(new {
                    browserVersion = environment.BrowserVersionString, anonymousProfile = true,
                    pages = pages, events = events, completed = true }));
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("mobile-probe", exception);
                File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString());
                Environment.ExitCode = 5;
            }
            finally
            {
                Console.WriteLine("MOBILE_PROBE_OUTPUT=" + output);
                Close();
            }
        }

        private async Task NavigateAsync(string url)
        {
            navigation = new TaskCompletionSource<bool>();
            view.CoreWebView2.Navigate(url);
            if (await Task.WhenAny(navigation.Task, Task.Delay(25000)) != navigation.Task)
            {
                Record("navigation timeout", url, null);
                view.CoreWebView2.Stop();
            }
            await Task.Delay(7000);
        }

        private async Task CaptureAsync(string name)
        {
            string snapshot = await view.ExecuteScriptAsync(SnapshotScript);
            File.WriteAllText(Path.Combine(output, name + ".json"), snapshot);
            using (FileStream stream = File.Create(Path.Combine(output, name + ".png")))
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            pages.Add(new { name = name, url = view.CoreWebView2.Source, zoomFactor = view.ZoomFactor,
                clientWidth = view.ClientSize.Width, clientHeight = view.ClientSize.Height, snapshot = json.DeserializeObject(snapshot) });
            Console.WriteLine("captured " + name + " " + view.CoreWebView2.Source);
        }

        private async Task ProbeFeedAsync(string mode)
        {
            // 仅关闭独立匿名会话的首用教学与首页选择，不碰真实用户的偏好设置。
            await view.ExecuteScriptAsync(@"(() => { for(const text of ['我知道了','不再提示','保存']) {
              const e=Array.from(document.querySelectorAll('button,div,span')).find(e=>e.children.length===0&&e.textContent.trim()===text);
              if(e)e.click(); } })()");
            await Task.Delay(1500);
            await CaptureAsync(mode + "-feed-ready");
            Dictionary<string, object> size = json.Deserialize<Dictionary<string, object>>(await view.ExecuteScriptAsync("({w:innerWidth,h:innerHeight})"));
            int x = Convert.ToInt32(size["w"]) / 2, y = Convert.ToInt32(size["h"]) / 2;
            foreach (string method in new[] { "touch", "wheel", "keyboard" })
            {
                string before = await view.ExecuteScriptAsync(FeedSampleScript);
                if (method == "touch")
                    await Cdp("Input.synthesizeScrollGesture", new Dictionary<string, object> {
                        { "x", x }, { "y", y }, { "yDistance", -y }, { "gestureSourceType", "touch" }, { "speed", 1200 } });
                else if (method == "wheel")
                    await Cdp("Input.dispatchMouseEvent", new Dictionary<string, object> {
                        { "type", "mouseWheel" }, { "x", x }, { "y", y }, { "deltaX", 0 }, { "deltaY", 600 } });
                else
                {
                    await Cdp("Input.dispatchKeyEvent", new Dictionary<string, object> {
                        { "type", "keyDown" }, { "key", "ArrowDown" }, { "code", "ArrowDown" }, { "windowsVirtualKeyCode", 40 } });
                    await Cdp("Input.dispatchKeyEvent", new Dictionary<string, object> {
                        { "type", "keyUp" }, { "key", "ArrowDown" }, { "code", "ArrowDown" }, { "windowsVirtualKeyCode", 40 } });
                }
                await Task.Delay(2500);
                string after = await view.ExecuteScriptAsync(FeedSampleScript);
                Record("feed input", view.CoreWebView2.Source, new { mode = mode, method = method,
                    before = json.DeserializeObject(before), after = json.DeserializeObject(after) });
                await CaptureAsync(mode + "-feed-" + method);
            }
        }

        private const string FeedSampleScript = @"(() => {
          const v=Array.from(document.querySelectorAll('video')).find(v=>!v.paused&&v.videoWidth>0);
          if(!v)return null;
          let hash=0;for(const c of v.currentSrc)hash=(hash*31+c.charCodeAt(0))|0;
          return {id:hash,w:v.videoWidth,h:v.videoHeight,duration:v.duration,time:v.currentTime};
        })()";

        private const string SnapshotScript = @"(() => {
  const visible = e => { const r=e.getBoundingClientRect(); return r.width>0 && r.height>0 && r.bottom>0 && r.top<innerHeight; };
  const selector = e => e.tagName.toLowerCase() + (e.id?'#'+e.id:'') + (typeof e.className==='string'?'.'+e.className.trim().split(/\s+/).join('.'):'');
  return {url:location.href,title:document.title,ua:navigator.userAgent,mobile:navigator.userAgentData?navigator.userAgentData.mobile:null,
    platform:navigator.platform,touchPoints:navigator.maxTouchPoints,coarse:matchMedia('(pointer:coarse)').matches,
    dpr:devicePixelRatio,width:innerWidth,height:innerHeight,viewport:document.querySelector('meta[name=viewport]')?.content,
    text:document.body?document.body.innerText.slice(0,12000):'',
    videos:Array.from(document.querySelectorAll('video')).map(v=>({w:v.videoWidth,h:v.videoHeight,visible:visible(v),paused:v.paused,time:v.currentTime,
      rect:{w:v.getBoundingClientRect().width,h:v.getBoundingClientRect().height}})),
    appPrompts:Array.from(document.querySelectorAll('a,button,div,span')).filter(e=>visible(e)&&e.children.length===0&&/打开.*[Aa][Pp][Pp]|打开抖音|下载抖音|立即体验/.test(e.textContent)).slice(0,30).map(e=>({text:e.textContent,selector:selector(e),parent:selector(e.parentElement)})),
    links:Array.from(document.querySelectorAll('a[href]')).slice(0,100).map(e=>({text:e.innerText.slice(0,80),href:e.href}))};
})()";
    }
}
