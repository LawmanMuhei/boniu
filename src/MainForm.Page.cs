using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsTestMode
                && (string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase)
                    || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))) { navigationGeneration++; return; }
            if (!WindowRules.IsDouyinUrl(e.Uri)) e.Cancel = true;
            else
            {
                navigationGeneration++;
                pageStyleRevision++;
                pageStyleFallback = false;
                navigationFailed = false;
                RefreshPageNotice();
                statusDot.State = StatusDotState.Loading;
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (WindowRules.IsDouyinUrl(e.Uri)) webView.Source = new Uri(e.Uri);
        }

        private async Task DetectLiveStateAsync()
        {
            if (!webViewReady || liveDetectionPending || webView.CoreWebView2 == null || !Visible || settingsOpen || isQuitting) return;
            int navigation = navigationGeneration;
            long operation = lifecycle.Generation;
            liveDetectionPending = true;
            bool hasLivePlayer = false;
            try
            {
                string result = await webView.ExecuteScriptAsync(PageScripts.LiveProbeScript);
                hasLivePlayer = string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("DetectLiveState", exception);
            }
            finally
            {
                liveDetectionPending = false;
            }

            if (isQuitting || IsDisposed || navigation != navigationGeneration || !lifecycle.IsCurrent(operation) || !Visible || settingsOpen) return;
            bool isLiveUrl = WindowRules.IsLiveUrl(webView.Source == null ? string.Empty : webView.Source.AbsoluteUri);
            UpdateLiveState(hasLivePlayer || isLiveUrl);
        }

        private void UpdateLiveState(bool nextIsLive)
        {
            if (nextIsLive == currentIsLive) return;
            Diagnostics.Log("UpdateLiveState " + currentIsLive + " -> " + nextIsLive);
            bool wasLive = currentIsLive;
            currentIsLive = nextIsLive;
            if (currentIsLive) ApplyLiveLayout();
            else if (wasLive || liveLandscapeApplied) RestorePortraitBounds();
            UpdateSettingsUi();
        }

        private void ApplyLiveLayout()
        {
            if (!settings.AutoLandscapeLive || !currentIsLive || liveLandscapeApplied) return;
            settings.NormalBounds = BoundsData.FromRectangle(Bounds);
            liveLandscapeApplied = true;
            MinimumSize = new Size(640, 360);
            Rectangle workArea = Screen.FromRectangle(Bounds).WorkingArea;
            Rectangle target = settings.LiveBounds == null
                ? WindowRules.CalculateLandscapeBounds(Bounds, workArea)
                : WindowRules.KeepOnScreen(settings.LiveBounds, 640, 360, new Size(800, 450));
            SetBoundsProgrammatically(target);
            ScheduleSettingsSave();
        }

        private void RestorePortraitBounds()
        {
            if (!liveLandscapeApplied) return;
            liveLandscapeApplied = false;
            MinimumSize = new Size(280, 460);
            Rectangle target = WindowRules.KeepOnScreen(settings.NormalBounds, 280, 460, WindowRules.DefaultSize);
            SetBoundsProgrammatically(target);
            ScheduleSettingsSave();
        }

        private void SetBoundsProgrammatically(Rectangle value)
        {
            Diagnostics.Mark("SetBoundsProgrammatically " + value);
            changingBounds = true;
            Bounds = value;
            changingBounds = false;
        }

        private void RememberCurrentBounds()
        {
            if (changingBounds || WindowState != FormWindowState.Normal) return;
            if (liveLandscapeApplied) settings.LiveBounds = BoundsData.FromRectangle(Bounds);
            else settings.NormalBounds = BoundsData.FromRectangle(Bounds);
            ScheduleSettingsSave();
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (isQuitting || IsDisposed || !settings.ImmersiveMode) return;
            if (!IsTestMode && !WindowRules.IsDouyinUrl(e.Source)) return;
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (message != "boniu:immersive-fallback:" + pageStyleRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)) return;
            pageStyleFallback = true;
            Diagnostics.Log("immersive-health fallback-to-native-layout");
            UpdateSettingsUi();
            RefreshPageNotice();
        }

        private void ReapplyPageStyles()
        {
            pageStyleFallback = false;
            navigationFailed = false;
            if (settingsOpen) ToggleSettings();
            else ApplyPageElementVisibility();
            UpdateSettingsUi();
            RefreshPageNotice();
        }

        private void RefreshPageNotice()
        {
            bool hasTransient = !string.IsNullOrEmpty(transientNotice);
            pageNotice.Text = hasTransient ? transientNotice
                : navigationFailed ? "页面加载失败 · 点击重试"
                : "已恢复标准布局 · 点击重试清爽样式";
            pageNotice.Visible = hasTransient || (!settingsOpen && (navigationFailed || pageStyleFallback));
            if (pageNotice.Visible) pageNotice.BringToFront();
            toolTip.SetToolTip(pageNotice, hasTransient ? transientNotice
                : navigationFailed ? "请检查网络连接，然后点击重试。"
                : "网页布局发生变化，已停用注入样式以保护播放。也可在设置中关闭清爽模式。");
        }

        private void ShowTransientNotice(string message)
        {
            // 行内横幅提示：不使用模态对话框，老板键隐藏时不会留下独立窗口。
            transientNotice = message;
            RefreshPageNotice();
            noticeTimer.Stop();
            noticeTimer.Start();
        }

        private void ClearTransientNotice()
        {
            transientNotice = null;
            noticeTimer.Stop();
            RefreshPageNotice();
        }

        private void ApplyPageElementVisibility()
        {
            ObserveTask(ApplyPageElementVisibilityAsync(), "ApplyPageElementVisibility");
        }

        private async Task ApplyPageElementVisibilityAsync()
        {
            Diagnostics.Mark("ApplyPageElementVisibility enter");
            if (!webViewReady || webView.CoreWebView2 == null || isQuitting || pageStyleFallback) return;
            if (settingsOpen || !webView.Visible)
            {
                Diagnostics.Mark("ApplyPageElementVisibility deferred while WebView hidden");
                return;
            }
            await webView.ExecuteScriptAsync(PageScripts.BuildVisibility(settings, ++pageStyleRevision));
            Diagnostics.Mark("ApplyPageElementVisibility done");
        }

        private bool ToolbarCollapsed
        {
            get
            {
                if (settings == null) return false;
                // The Settings button must remain available while configuring immersive mode. The
                // toolbar collapses after returning to the page, when the WebView is visible again.
                return settings.ChromeHidden || (settings.ImmersiveMode && !settingsOpen && !toolbarRevealed);
            }
        }

        private void UpdateImmersiveToolbar()
        {
            if (!settings.ImmersiveMode || settings.ChromeHidden || !Visible) return;
            Point cursor = PointToClient(Cursor.Position);
            bool nearTop = cursor.X >= 0 && cursor.X <= ClientSize.Width
                && cursor.Y >= 0 && cursor.Y <= Scaled(ToolbarRevealHeight);
            if (nearTop == toolbarRevealed) return;
            toolbarRevealed = nearTop;
            Diagnostics.Log("immersive-toolbar reveal=" + nearTop + " cursorY=" + cursor.Y);
            LayoutWindow();
        }

        private void ToggleImmersiveMode()
        {
            pageStyleFallback = false;
            settings.ImmersiveMode = !settings.ImmersiveMode;
            RefreshPageNotice();
            toolbarRevealed = false;
            Diagnostics.Log("immersive=" + settings.ImmersiveMode + " source=hotkey");
            ScheduleSettingsSave();
            LayoutWindow();
            UpdateSettingsUi();
            ApplyPageElementVisibility();
        }

    }
}
