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
        private void MonitorPointer(object sender, EventArgs e)
        {
            Diagnostics.Tick();
            Diagnostics.Mark("pointer");
            UpdateImmersiveToolbar();
            if (IsTestMode || domProbe || !settings.AutoHideEnabled || !CanRestoreWindow
                || suppressInactiveHide || captureTarget != null || !Visible
                || DateTime.UtcNow < showGraceUntil) return;
            if (Control.MouseButtons != MouseButtons.None)
            {
                outsideSince = null;
                return;
            }
            NativeMethods.NativeRectangle nativeBounds;
            bool hasNativeBounds = NativeMethods.GetWindowRect(Handle, out nativeBounds);
            Point cursor = Cursor.Position;
            int margin = Scaled(AutoHidePointerMargin);
            bool inside = hasNativeBounds
                ? cursor.X >= nativeBounds.Left - margin && cursor.X < nativeBounds.Right + margin
                    && cursor.Y >= nativeBounds.Top - margin && cursor.Y < nativeBounds.Bottom + margin
                : new Rectangle(Bounds.X - margin, Bounds.Y - margin,
                    Bounds.Width + margin * 2, Bounds.Height + margin * 2).Contains(cursor);
            if (inside)
            {
                outsideSince = null;
                return;
            }
            if (!outsideSince.HasValue) outsideSince = DateTime.UtcNow.AddMilliseconds(-pointerTimer.Interval);
            if ((DateTime.UtcNow - outsideSince.Value).TotalMilliseconds >= settings.AutoHideDelayMilliseconds) HideWindow();
        }

        private void HideWindow()
        {
            if (!Visible || isQuitting) return;
            DismissOwnedPopups();
            Stopwatch elapsed = Stopwatch.StartNew();
            lifecycle.SetVisible(false);
            outsideSince = null;
            // Hide the native window before any potentially slow WebView/DOM call.
            Hide();
            double hiddenMs = elapsed.Elapsed.TotalMilliseconds;
            webView.Visible = false;
            webView.SynchronizeVisibility(); // Controller must be invisible for suspension and to stop compositing.
            liveTimer.Stop();
            pointerTimer.Interval = 250; // Keep the watchdog heartbeat without a 100 Hz hidden poll.
            ApplyMediaState(); // Native mute first; no await on the window-hiding path.
            if (captureTarget != null) CancelShortcutCapture();
            if (settingsOpen)
            {
                settingsOpen = false;
                settingsPanel.Visible = false;
                webView.Visible = false;
            }
            ArmSuspendTimer(WebViewSuspendDelayMilliseconds);
            Diagnostics.Log("hide native-ms=" + hiddenMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " mute-dispatch-ms=" + elapsed.Elapsed.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        private void ApplyMediaState()
        {
            ObserveTask(ApplyMediaStateAsync(), "ApplyMediaState");
        }

        private async Task ApplyMediaStateAsync()
        {
            if (!webViewReady || isQuitting || IsDisposed || webView.CoreWebView2 == null) return;
            long operation = lifecycle.Refresh();
            bool playing = Visible;
            webView.CoreWebView2.IsMuted = !playing || settings.Muted;
            if (webViewSuspended) return;
            await webView.ExecuteScriptAsync(MediaScripts.Build(operation, playing));
            if (lifecycle.IsCurrent(operation)) Diagnostics.Mark("media state applied visible=" + playing);
        }

        private void DismissOwnedPopups()
        {
            // 老板键安全网：主窗隐藏前关闭全部 owned 弹窗（含模态 MessageBox），
            // 避免主窗消失后对话框单独留在屏幕上。按“取消”语义关闭，确认类操作默认不执行。
            int closed = 0;
            try
            {
                foreach (Form owned in OwnedForms)
                {
                    if (owned == null || owned.IsDisposed || !owned.Visible) continue;
                    owned.Close();
                    closed++;
                }
                NativeMethods.EnumThreadWindows(NativeMethods.GetCurrentThreadId(),
                    delegate(IntPtr windowHandle, IntPtr parameter)
                    {
                        if (windowHandle != Handle && NativeMethods.GetWindow(windowHandle, NativeMethods.GW_OWNER) == Handle
                            && NativeMethods.IsWindowVisible(windowHandle))
                        {
                            if (!NativeMethods.EndDialog(windowHandle, new IntPtr(2)))
                                NativeMethods.PostMessage(windowHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            closed++;
                        }
                        return true;
                    }, IntPtr.Zero);
            }
            catch (Exception exception) { Diagnostics.LogException("DismissOwnedPopups", exception); }
            if (closed > 0) Diagnostics.Log("dismissed-owned-popups count=" + closed);
        }

        private void HideWindowSafely()
        {
            if (CanRestoreWindow) HideWindow();
            else ShowOwnedMessage("显示快捷键不可用，请先在设置中修改快捷键，或重新打开程序恢复窗口。", "波妞摸鱼", MessageBoxIcon.Warning);
        }

        private void ShowWindow()
        {
            if (isQuitting || IsDisposed) return;
            Stopwatch elapsed = Stopwatch.StartNew();
            lifecycle.SetVisible(true);
            suspendTimer.Stop();
            showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
            outsideSince = null;
            Show();
            webView.Visible = !settingsOpen;
            webView.SynchronizeVisibility();
            Activate();
            pointerTimer.Interval = 10;
            LayoutWindow();
            ResumeSuspendedWebView();
            ApplyMediaState();
            ApplyPageElementVisibility();
            if (webViewReady) liveTimer.Start();
            Diagnostics.Log("show dispatch-ms=" + elapsed.Elapsed.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        private async Task SuspendWebViewIfHiddenAsync()
        {
            if (Visible || webViewSuspended || webViewSuspendPending || isQuitting) return;
            if (!webViewReady || webView.CoreWebView2 == null)
            {
                ArmSuspendTimer(2000);
                return;
            }
            long operation = lifecycle.Generation;
            bool retry = false;
            webViewSuspendPending = true;
            try
            {
                webView.SynchronizeVisibility();
                bool suspended = await webView.CoreWebView2.TrySuspendAsync();
                if (isQuitting || IsDisposed) return;
                webViewSuspended = suspended;
                // A show/hide or navigation while TrySuspendAsync was pending invalidates its result.
                if (suspended && (Visible || !lifecycle.IsCurrent(operation)))
                {
                    ResumeSuspendedWebView();
                    ApplyMediaState();
                    retry = !Visible;
                }
                else if (!suspended) retry = !Visible;
                Diagnostics.Log("webview-suspend success=" + suspended + " hidden=" + (!Visible));
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("SuspendWebView", exception);
                retry = !Visible;
            }
            finally
            {
                webViewSuspendPending = false;
                if (retry && !Visible && !isQuitting && !IsDisposed) ArmSuspendTimer(5000);
            }
        }

        private void ArmSuspendTimer(int delayMilliseconds)
        {
            if (isQuitting || IsDisposed || Visible) return;
            suspendTimer.Stop();
            suspendTimer.Interval = delayMilliseconds;
            suspendTimer.Start();
        }

        private void ResumeSuspendedWebView()
        {
            if (!webViewSuspended || !webViewReady || webView.CoreWebView2 == null) return;
            try
            {
                webView.CoreWebView2.Resume();
                webViewSuspended = false;
                Diagnostics.Log("webview-resume");
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("ResumeWebView", exception);
            }
        }

        private void ToggleWindow()
        {
            if (Visible) HideWindow();
            else ShowWindow();
        }

        private void ToggleAlwaysOnTop()
        {
            TopMost = !TopMost;
            settings.AlwaysOnTop = TopMost;
            ScheduleSettingsSave();
            UpdateToolbarState();
        }

        private void HideWhenApplicationBecomesInactive()
        {
            if (!settings.HideWhenInactive || suppressInactiveHide || !CanRestoreWindow
                || captureTarget != null || !Visible || isQuitting) return;
            HideWindow();
        }

        private void SubscribeSessionEvents()
        {
            if (IsTestMode || sessionEventsSubscribed) return;
            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                sessionEventsSubscribed = true;
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("SubscribeSessionEvents", exception);
            }
        }

        private void UnsubscribeSessionEvents()
        {
            if (!sessionEventsSubscribed) return;
            try { SystemEvents.SessionSwitch -= OnSessionSwitch; }
            catch (Exception exception) { Diagnostics.LogException("UnsubscribeSessionEvents", exception); }
            sessionEventsSubscribed = false;
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason != SessionSwitchReason.SessionLock
                && e.Reason != SessionSwitchReason.ConsoleDisconnect
                && e.Reason != SessionSwitchReason.RemoteDisconnect) return;
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (!isQuitting && !IsDisposed && Visible) HideWindow();
            });
        }

    }
}
