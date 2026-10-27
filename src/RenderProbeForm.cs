using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniView.WebView2App
{
    // 仅 --render-probe 启用：在同一个小窗里对比原生渲染与超采样渲染，截的是屏幕上真实显示的像素。
    internal sealed class RenderProbeForm : Form
    {
        private readonly WebView2 view = new WebView2();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<object> results = new List<object>();
        private readonly string output;
        private readonly string profile;

        private const string MetricsScript = @"(() => {
          const imgs=[...document.querySelectorAll('img')].filter(i=>{const r=i.getBoundingClientRect();return r.width>24&&r.bottom>0&&r.top<innerHeight;})
            .slice(0,12).map(i=>({nw:i.naturalWidth,rw:Math.round(i.getBoundingClientRect().width*devicePixelRatio)}));
          const v=[...document.querySelectorAll('video')].find(x=>{const r=x.getBoundingClientRect();return r.height>0&&r.top>-10&&r.top<innerHeight/2;});
          const digg=document.querySelector('[data-e2e=""video-player-digg""]');const dr=digg?digg.getBoundingClientRect():null;
          return {dpr:devicePixelRatio,w:innerWidth,h:innerHeight,scrollW:document.documentElement.scrollWidth,digg:dr?[Math.round(dr.left),Math.round(dr.right)]:null,imgs:imgs,video:v?[v.videoWidth,v.videoHeight]:null};
        })()";

        private const string PlaybackScript = @"(async () => {
          const v=[...document.querySelectorAll('video')].find(x=>{const r=x.getBoundingClientRect();return r.height>0&&r.top>-10&&r.top<innerHeight/2;});
          if(!v)return {error:'no video'};
          v.muted=true;try{await v.play();}catch(e){}
          const q0=v.getVideoPlaybackQuality();let frames=0;const t0=performance.now();
          await new Promise(done=>{const tick=()=>{frames++;if(performance.now()-t0<5000)requestAnimationFrame(tick);else done();};requestAnimationFrame(tick);});
          const q1=v.getVideoPlaybackQuality();v.pause();
          return {fps:Math.round(frames*1000/(performance.now()-t0)),dropped:q1.droppedVideoFrames-q0.droppedVideoFrames,decoded:q1.totalVideoFrames-q0.totalVideoFrames};
        })()";

        internal RenderProbeForm(string outputFolder)
        {
            output = Path.GetFullPath(string.IsNullOrEmpty(outputFolder)
                ? Path.Combine(Path.GetTempPath(), "BoniuRenderProbe", DateTime.Now.ToString("yyyyMMdd-HHmmss")) : outputFolder);
            profile = Path.Combine(Path.GetTempPath(), "BoniuRenderProbeProfile", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            Text = "波妞摸鱼 · 渲染清晰度对比探针（独立匿名会话）";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 40);
            ClientSize = new Size(420, 760);
            TopMost = true;
            view.Dock = DockStyle.Fill;
            Controls.Add(view);
            Shown += async delegate { await RunAsync(); };
            FormClosed += delegate { view.Dispose(); };
        }

        private Task<string> Cdp(string method, Dictionary<string, object> arguments)
        {
            return view.CoreWebView2.CallDevToolsProtocolMethodAsync(method, json.Serialize(arguments));
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
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { e.Cancel = !WindowRules.IsDouyinUrl(e.Uri); };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                core.LaunchingExternalUriScheme += delegate(object sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e) { e.Cancel = true; };
                view.ZoomFactor = 1.0;
                core.Navigate(WindowRules.DesktopHomeUrl);
                await Task.Delay(9000);
                await view.ExecuteScriptAsync(PageScripts.OpenRecommendScript);
                await Task.Delay(9000);
                // 匿名会话会弹登录框，挡住画面；只为截图移除，不影响任何真实数据。
                await view.ExecuteScriptAsync(@"(() => { document.querySelectorAll('[id*=login],[class*=login-mask],[class*=LoginMask],[class*=modal]').forEach(e=>{ if(e.getBoundingClientRect().width>200) e.remove(); }); })()");
                await view.ExecuteScriptAsync(PageScripts.BuildClarity(true));
                await Task.Delay(4000);

                await MeasureAsync(environment, "native", null);
                await MeasureAsync(environment, "dsf2", 2.0);
                // 抖音电脑版 body 带 min-width:580px，小窗 100% 时右侧被裁；对比解除限制与缩小适配两种做法。
                await view.ExecuteScriptAsync(@"(() => { const s=document.createElement('style'); s.id='__probeMinWidth';
                  s.textContent='html,body{min-width:0!important;overflow-x:hidden!important}'; document.head.appendChild(s); })()");
                await MeasureAsync(environment, "minwidth0", null);
                await MeasureAsync(environment, "minwidth0-immersive", null, PageScripts.BuildVisibility(new AppSettings { ImmersiveMode = true }, 1));
                await view.ExecuteScriptAsync("(() => { const s=document.getElementById('__probeMinWidth'); if(s) s.remove(); })()");
                await view.ExecuteScriptAsync(PageScripts.BuildVisibility(new AppSettings(), 2));
                view.ZoomFactor = 0.63;
                await MeasureAsync(environment, "fit063", null);
                view.ZoomFactor = 1.0;
                File.WriteAllText(Path.Combine(output, "result.json"), json.Serialize(results));
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "error.txt"), exception.ToString());
                Environment.ExitCode = 6;
            }
            Close();
        }

        private Task MeasureAsync(CoreWebView2Environment environment, string name, double? scale)
        {
            return MeasureAsync(environment, name, scale, null);
        }

        private async Task MeasureAsync(CoreWebView2Environment environment, string name, double? scale, string prepare)
        {
            if (prepare != null) await view.ExecuteScriptAsync(prepare);
            if (scale.HasValue)
                await Cdp("Emulation.setDeviceMetricsOverride", new Dictionary<string, object> {
                    { "width", 0 }, { "height", 0 }, { "deviceScaleFactor", scale.Value }, { "mobile", false } });
            else
                await Cdp("Emulation.clearDeviceMetricsOverride", new Dictionary<string, object>());
            await Task.Delay(5000);
            // ExecuteScriptAsync 不等待 Promise，测帧率要用 Runtime.evaluate 的 awaitPromise。
            string playback = await Cdp("Runtime.evaluate", new Dictionary<string, object> {
                { "expression", PlaybackScript }, { "awaitPromise", true }, { "returnByValue", true } });
            await Task.Delay(1500);
            string metrics = await view.ExecuteScriptAsync(MetricsScript);
            long memory = 0;
            try
            {
                foreach (CoreWebView2ProcessInfo info in environment.GetProcessInfos())
                {
                    try { memory += Process.GetProcessById(info.ProcessId).PrivateMemorySize64; }
                    catch (ArgumentException) { }
                }
            }
            catch (Exception) { }
            Rectangle bounds = RectangleToScreen(ClientRectangle);
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bitmap.Size);
                bitmap.Save(Path.Combine(output, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            results.Add(new { name = name, scale = scale, playback = playback, metrics = metrics,
                webviewPrivateMB = memory / (1024 * 1024), client = bounds.Size.ToString() });
        }
    }
}
