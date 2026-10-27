using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private readonly ToggleSwitch desktopAutoFitToggle = new ToggleSwitch();
        private readonly Label desktopAutoFitStatus = CreateStatusLabel();
        private readonly Panel zoomEditor = new Panel();
        private readonly RoundedButton zoomMinusButton = CreateSettingsActionButton("−", false);
        private readonly RoundedButton zoomPlusButton = CreateSettingsActionButton("+", false);
        private readonly RoundedButton zoomResetButton = CreateSettingsActionButton("重置", false);
        private readonly Label zoomValue = new Label();
        private readonly RoundedButton zoomNotice = CreateSettingsActionButton("", false);
        private readonly Timer zoomNoticeTimer = new Timer();
        private bool applyingZoom;

        private const string ZoomResetScript = @"(() => {
          if(window.__boniuZoomKeys)return;window.__boniuZoomKeys=true;
          addEventListener('keydown',e=>{
            if(e.ctrlKey&&!e.altKey&&(e.key==='0'||e.code==='Numpad0')){
              e.preventDefault();e.stopImmediatePropagation();
              chrome.webview.postMessage({type:'boniu-zoom-reset'});
            }
          },true);
        })()";

        private double ZoomFitFactor
        {
            get { return CurrentSiteMode == SiteMode.Desktop && settings.DesktopAutoFit
                ? WindowRules.CalculateZoomFactor(ClientSize.Width) : 1.0; }
        }

        private int UserZoomPercent
        {
            get { return CurrentSiteMode == SiteMode.Mobile ? settings.MobileZoomPercent : settings.DesktopZoomPercent; }
            set
            {
                int next = WindowRules.NormalizeZoomPercent(value);
                if (CurrentSiteMode == SiteMode.Mobile) settings.MobileZoomPercent = next;
                else settings.DesktopZoomPercent = next;
            }
        }

        private void BuildClaritySettings(SettingsCard card)
        {
            AddInlineRow(card, "电脑版自动适配宽度", desktopAutoFitStatus, desktopAutoFitToggle, 44, 26);
            desktopAutoFitToggle.CheckedChanged += delegate
            {
                if (settings.DesktopAutoFit == desktopAutoFitToggle.Checked) return;
                settings.DesktopAutoFit = desktopAutoFitToggle.Checked;
                UpdateZoom();
                ScheduleSettingsSave();
                UpdateSettingsUi();
            };
        }

        private void UpdateZoom()
        {
            if (!webViewReady) return;
            applyingZoom = true;
            try
            {
                webView.ZoomFactor = WindowRules.CalculateEffectiveZoom(CurrentSiteMode,
                    settings.DesktopAutoFit, ClientSize.Width, UserZoomPercent);
            }
            finally { applyingZoom = false; }
        }

        private void BuildZoomSettings(SettingsCard card)
        {
            AddStackedRow(card, "页面缩放 · Ctrl + 滚轮", zoomEditor, 32);
            zoomEditor.BackColor = UiPalette.Card;
            zoomValue.TextAlign = ContentAlignment.MiddleCenter;
            zoomValue.Font = UiFonts.Caption;
            zoomValue.ForeColor = UiPalette.TextPrimary;
            zoomEditor.Controls.AddRange(new Control[] { zoomMinusButton, zoomValue, zoomPlusButton, zoomResetButton });
            zoomMinusButton.AccessibleName = "缩小页面";
            zoomPlusButton.AccessibleName = "放大页面";
            zoomResetButton.AccessibleName = "页面缩放恢复 100%";
            zoomMinusButton.Click += delegate { SetUserZoom(UserZoomPercent - 10, true); };
            zoomPlusButton.Click += delegate { SetUserZoom(UserZoomPercent + 10, true); };
            zoomResetButton.Click += delegate { SetUserZoom(100, true); };
            zoomEditor.Resize += delegate
            {
                int gap = Math.Max(2, Scaled(4));
                int available = Math.Max(4, zoomEditor.Width - gap * 3);
                int side = available / 5, reset = available * 3 / 10;
                int value = available - side * 2 - reset;
                zoomMinusButton.SetBounds(0, 0, side, zoomEditor.Height);
                zoomValue.SetBounds(side + gap, 0, value, zoomEditor.Height);
                zoomPlusButton.SetBounds(side + value + gap * 2, 0, side, zoomEditor.Height);
                zoomResetButton.SetBounds(side * 2 + value + gap * 3, 0, reset, zoomEditor.Height);
            };
            zoomNotice.Visible = false;
            zoomNotice.AccessibleName = "当前页面缩放";
            zoomNotice.Cursor = Cursors.Default;
            zoomNotice.TabStop = false;
            zoomNotice.AccessibleRole = AccessibleRole.StaticText;
            contentHost.Controls.Add(zoomNotice);
            zoomNoticeTimer.Interval = 1200;
            zoomNoticeTimer.Tick += delegate { zoomNoticeTimer.Stop(); zoomNotice.Visible = false; };
            FormClosed += delegate { zoomNoticeTimer.Dispose(); };
        }

        private void SetUserZoom(int percent, bool showNotice)
        {
            UserZoomPercent = percent;
            UpdateZoom();
            ScheduleSettingsSave();
            UpdateSettingsUi();
            if (!showNotice || settingsOpen) return;
            int width = Math.Min(contentHost.Width, Scaled(132));
            zoomNotice.SetBounds(Math.Max(0, (contentHost.Width - width) / 2), Scaled(12), width, Scaled(32));
            zoomNotice.Text = "缩放 " + UserZoomPercent + "%";
            zoomNotice.Visible = true;
            zoomNotice.BringToFront();
            zoomNoticeTimer.Stop();
            zoomNoticeTimer.Start();
        }

        private void OnWebViewZoomFactorChanged(object sender, EventArgs e)
        {
            if (applyingZoom || !webViewReady || isQuitting || deviceProfileChanging) return;
            double userFactor = webView.ZoomFactor / ZoomFitFactor;
            SetUserZoom((int)Math.Round(userFactor * 100, MidpointRounding.AwayFromZero), true);
        }

        private bool HandleZoomMessage(CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!WindowRules.IsDouyinUrl(e.Source)) return false;
            // 旧版健康守卫发送字符串；只解析对象消息，不把不认识的类型当成缩放命令。
            string raw = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(raw) || raw[0] != '{') return false;
            Dictionary<string, object> message;
            try { message = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw); }
            catch (ArgumentException) { return false; }
            object type;
            if (message == null || !message.TryGetValue("type", out type) || !object.Equals(type, "boniu-zoom-reset")) return false;
            SetUserZoom(100, true);
            return true;
        }

        private async Task RunZoomRegressionAsync()
        {
            settings.DesktopAutoFit = true;
            double factor = ZoomFitFactor;
            webView.ZoomFactor = factor * 1.3;
            // 模拟来自浏览器的缩放事件，验证宿主换算及 Resize 后的保存逻辑。
            OnWebViewZoomFactorChanged(webView, EventArgs.Empty);
            UpdateZoom();
            AssertRegression(settings.DesktopZoomPercent == 130 && Math.Abs(webView.ZoomFactor - factor * 1.3) < 0.001,
                "external desktop zoom survives resize update");
            settings.SiteMode = "Mobile";
            await ApplyDeviceProfileAsync();
            SetUserZoom(150, false);
            AssertRegression(settings.MobileZoomPercent == 150 && settings.DesktopZoomPercent == 130 && Math.Abs(webView.ZoomFactor - 1.5) < 0.001,
                "mobile and desktop zoom independent");
            await webView.ExecuteScriptAsync("chrome.webview.postMessage({type:'boniu-zoom-reset'})");
            await WaitForConditionAsync(delegate { return settings.MobileZoomPercent == 100; }, "zoom reset message", 3000);
            AssertRegression(settings.MobileZoomPercent == 100 && settings.DesktopZoomPercent == 130, "Ctrl zero resets current mode only");
            SetUserZoom(400, false);
            AssertRegression(settings.MobileZoomPercent == 200 && Math.Abs(webView.ZoomFactor - 2.0) < 0.001, "user zoom upper limit enforced");
            SetUserZoom(0, false);
            AssertRegression(settings.MobileZoomPercent == 50 && Math.Abs(webView.ZoomFactor - 0.5) < 0.001, "user zoom lower limit enforced");
            SettingsStore zoomStore = new SettingsStore(Path.Combine(appFolder, "zoom-preferences-test.json"));
            zoomStore.Save(settings);
            AppSettings restored = zoomStore.Load();
            AssertRegression(restored.MobileZoomPercent == 50 && restored.DesktopZoomPercent == 130,
                "independent zoom preferences saved and loaded in isolated profile");
            settings.MobileZoomPercent = settings.DesktopZoomPercent = 100;
            settings.SiteMode = "Desktop";
            await ApplyDeviceProfileAsync();
            UpdateZoom();
            UpdateSettingsUi();
        }

        private async Task RunClarityRegressionAsync()
        {
            deviceProfileChanging = true;
            try
            {
                settings.SiteMode = "Mobile";
                await ApplyDeviceProfileAsync();
                UpdateZoom();
                AssertRegression(Math.Abs(webView.ZoomFactor - 1.0) < 0.001, "mobile default zoom is 100 percent");
                settings.SiteMode = "Desktop";
                await ApplyDeviceProfileAsync();
                settings.DesktopAutoFit = false;
                UpdateZoom();
                AssertRegression(Math.Abs(webView.ZoomFactor - 1.0) < 0.001, "desktop autofit can be disabled");
                settings.DesktopAutoFit = true;
                UpdateZoom();
                AssertRegression(webView.ZoomFactor >= 0.67, "desktop fit does not shrink below 67 percent");
            }
            finally
            {
                settings.SiteMode = "Desktop";
                settings.DesktopAutoFit = true;
                deviceProfileChanging = false;
                UpdateSettingsUi();
            }
        }
    }
}
