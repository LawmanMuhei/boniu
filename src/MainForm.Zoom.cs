using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private readonly ToggleSwitch desktopAutoFitToggle = new ToggleSwitch();
        private readonly Label desktopAutoFitStatus = CreateStatusLabel();

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
            if (webViewReady) webView.ZoomFactor = WindowRules.CalculateEffectiveZoom(CurrentSiteMode,
                settings.DesktopAutoFit, ClientSize.Width, UserZoomPercent);
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
