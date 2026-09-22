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
    internal sealed partial class MainForm : Form
    {
        private const string DouyinUrl = "https://www.douyin.com/?recommend=1";
        private const int ToolbarHeight = 36;
        private const int ToolbarButtonWidth = 30;
        private const int ToolbarButtonHeight = 28;
        private const int ToolbarButtonGap = 2;
        private const int ToolbarSidePadding = 6;
        private const int ToolbarRevealHeight = 44;
        private const int WindowCornerRadius = 10;
        private const int HotkeyWindow = 1;
        private const int HotkeyChrome = 2;
        private const int HotkeyMute = 3;
        private const int HotkeyImmersive = 4;
        private const int ShowGraceMilliseconds = 2200;
        private const int WebViewSuspendDelayMilliseconds = 10000;
        private const int AutoHidePointerMargin = 18;

        private readonly VisibilityAwareWebView webView = new VisibilityAwareWebView();
        private readonly Panel toolbar = new Panel();
        private readonly Panel contentHost = new Panel();
        private readonly DarkScrollPanel settingsPanel = new DarkScrollPanel();
        private readonly StatusDot statusDot = new StatusDot();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Timer pointerTimer = new Timer();
        private readonly Timer liveTimer = new Timer();
        private readonly Timer settingsSaveTimer = new Timer();
        private readonly Timer suspendTimer = new Timer();
        private readonly Timer noticeTimer = new Timer();
        private readonly RoundedButton backButton;
        private readonly RoundedButton reloadButton;
        private readonly RoundedButton overflowButton;
        private readonly ContextMenuStrip overflowMenu;
        private readonly RoundedButton closeButton;
        private readonly RoundedButton pinButton;
        private readonly RoundedButton muteButton;
        private readonly RoundedButton settingsButton;
        private readonly RoundedButton chromeButton;
        private readonly RoundedButton hideButton;
        private readonly RoundedButton windowShortcutButton = CreateShortcutButton();
        private readonly RoundedButton chromeShortcutButton = CreateShortcutButton();
        private readonly RoundedButton immersiveShortcutButton = CreateShortcutButton();
        private readonly RoundedButton muteShortcutButton = CreateShortcutButton();
        private readonly Label headingLabel = new Label();
        private readonly Label brandLabel = new Label();
        private readonly Label windowShortcutStatus = CreateStatusLabel();
        private readonly Label chromeShortcutStatus = CreateStatusLabel();
        private readonly Label immersiveShortcutStatus = CreateStatusLabel();
        private readonly Label muteShortcutStatus = CreateStatusLabel();
        private readonly Label autoHideStatus = CreateStatusLabel();
        private readonly Label inactiveHideStatus = CreateStatusLabel();
        private readonly Label liveStatus = CreateStatusLabel();
        private readonly Label dataStatus = CreateStatusLabel();
        private readonly Label updateStatus = CreateStatusLabel();
        private readonly ToggleSwitch autoHideToggle = new ToggleSwitch();
        private readonly ToggleSwitch inactiveHideToggle = new ToggleSwitch();
        private readonly ToggleSwitch trayToggle = new ToggleSwitch();
        private readonly Label trayStatus = CreateStatusLabel();
        private readonly ToggleSwitch landscapeToggle = new ToggleSwitch();
        private readonly ToggleSwitch leftNavToggle = new ToggleSwitch();
        private readonly ToggleSwitch topBarToggle = new ToggleSwitch();
        private readonly ToggleSwitch rightBarToggle = new ToggleSwitch();
        private readonly ToggleSwitch immersiveToggle = new ToggleSwitch();
        private readonly Label leftNavStatus = CreateStatusLabel();
        private readonly Label topBarStatus = CreateStatusLabel();
        private readonly Label rightBarStatus = CreateStatusLabel();
        private readonly Label immersiveStatus = CreateStatusLabel();
        private readonly SegmentedPicker autoHideDelayPicker = new SegmentedPicker();
        private readonly RoundedButton openDataButton = CreateSettingsActionButton("打开本地数据目录", false);
        private readonly RoundedButton resetSettingsButton = CreateSettingsActionButton("恢复程序默认设置", false);
        private readonly RoundedButton clearDataButton = CreateSettingsActionButton("清除登录和网页缓存", true);
        private readonly RoundedButton retryPageButton = CreateSettingsActionButton("重新应用页面样式", false);
        private readonly RoundedButton pageNotice = CreateSettingsActionButton("", false);
        private bool pageStyleFallback;
        private bool navigationFailed;
        private long pageStyleRevision;
        private string transientNotice;
        private UpdateInfo pendingUpdate;
        private readonly RoundedButton checkUpdateButton = CreateSettingsActionButton("检查更新", false);
        private readonly SettingsStore settingsStore;
        private readonly UpdateService updateService;
        private readonly List<SettingsCard> cards = new List<SettingsCard>();
        private SettingsRow delayRow;
        private float uiScale = 1F;
        private bool dwmRoundsCorners;
        private bool windowShapeProbed;
        private Size shapedSize = Size.Empty;
        private float shapedScale = -1F;
        private Region windowRegion;
        private int rowInset = 20;
        private int cardRadius = 12;
        private readonly string appFolder;
        private readonly string userDataFolder;
        private readonly bool smokeTest;
        private readonly bool liveSmokeTest;
        private readonly bool settingsSmokeTest;
        private readonly bool domProbe;
        private readonly string updateHealthToken;

        private AppSettings settings;
        private HotkeyDefinition windowHotkey;
        private HotkeyDefinition chromeHotkey;
        private HotkeyDefinition immersiveHotkey;
        private HotkeyDefinition muteHotkey;
        private bool windowHotkeyAvailable;
        private bool chromeHotkeyAvailable;
        private bool immersiveHotkeyAvailable;
        private bool muteHotkeyAvailable;
        private bool settingsOpen;
        private bool toolbarRevealed;
        private bool currentIsLive;
        private bool liveLandscapeApplied;
        private bool liveDetectionPending;
        private bool isQuitting;
        private bool changingBounds;
        private bool webViewReady;
        private bool webViewSuspended;
        private bool webViewSuspendPending;
        private readonly WindowLifecycle lifecycle = new WindowLifecycle();
        private int navigationGeneration;
        private bool IsTestMode { get { return smokeTest || liveSmokeTest || settingsSmokeTest || regressionTest; } }
        private readonly bool regressionTest;
        private bool suppressInactiveHide;
        private bool sessionEventsSubscribed;
        private bool updateCheckRunning;
        private string captureTarget;
        private DateTime? outsideSince;
        private DateTime showGraceUntil;

        internal MainForm(bool smokeTest, bool liveSmokeTest, bool settingsSmokeTest, bool domProbe,
            string updateHealthToken = null, bool regressionTest = false)
        {
            this.smokeTest = smokeTest;
            this.regressionTest = regressionTest;
            this.liveSmokeTest = liveSmokeTest;
            this.settingsSmokeTest = settingsSmokeTest;
            this.domProbe = domProbe;
            this.updateHealthToken = updateHealthToken;
            appFolder = IsTestMode
                ? Path.Combine(Path.GetTempPath(), "MiniViewWebView2Smoke", Process.GetCurrentProcess().Id.ToString())
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniViewWebView2");
            userDataFolder = IsTestMode
                ? Path.Combine(Path.GetTempPath(), "MiniViewWebView2Smoke", Process.GetCurrentProcess().Id.ToString())
                : Path.Combine(appFolder, "UserData");
            settingsStore = new SettingsStore(Path.Combine(appFolder, "settings.json"));
            updateService = new UpdateService(appFolder);
            settings = IsTestMode ? new AppSettings() : settingsStore.Load();
            if (liveSmokeTest) settings.AutoLandscapeLive = true;

            windowHotkey = ParseShortcut(settings.HideShortcut, Keys.D);
            chromeHotkey = ParseShortcut(settings.ChromeShortcut, Keys.B);
            muteHotkey = ParseShortcut(settings.MuteShortcut, Keys.M);
            immersiveHotkey = ParseShortcut(settings.ImmersiveShortcut, Keys.F);

            Text = "波妞摸鱼";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = WindowRules.KeepOnScreen(settings.NormalBounds, 280, 460, WindowRules.DefaultSize);
            MinimumSize = new Size(280, 460);
            BackColor = UiPalette.Shell;
            ForeColor = Color.FromArgb(244, 244, 245);
            ShowInTaskbar = false;
            TopMost = settings.AlwaysOnTop;
            KeyPreview = true;
            DoubleBuffered = true;

            toolbar.BackColor = UiPalette.Toolbar;
            toolbar.Height = ToolbarHeight;
            toolbar.MouseDown += DragWindow;
            toolbar.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(UiPalette.ToolbarHover))
                    e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            };

            backButton = CreateToolbarButton("\uE72B", "后退", NavigateBack);
            reloadButton = CreateToolbarButton("\uE72C", "刷新", delegate { if (webViewReady) webView.Reload(); });
            pinButton = CreateToolbarButton("\uE718", "取消置顶", ToggleAlwaysOnTop);
            muteButton = CreateToolbarButton("\uE767", "静音", ToggleMute);
            settingsButton = CreateToolbarButton("\uE713", "设置", ToggleSettings);
            chromeButton = CreateToolbarButton("\uE8A7", "隐藏边框", ToggleChrome);
            hideButton = CreateToolbarButton("\uE70D", "立即隐藏", HideWindowSafely);
            closeButton = CreateToolbarButton("\uE8BB", "退出", QuitApplication);
            closeButton.HoverFaceColor = UiPalette.CloseHover;
            closeButton.PressedFaceColor = UiPalette.ClosePressed;
            closeButton.ActiveForeColor = UiPalette.TextPrimary;
            overflowButton = CreateToolbarButton("\uE712", "更多", ShowOverflowMenu);
            overflowButton.Visible = false;
            overflowMenu = new ContextMenuStrip();
            overflowMenu.ShowImageMargin = false;
            overflowMenu.BackColor = UiPalette.Toolbar;
            overflowMenu.ForeColor = UiPalette.ToolbarGlyph;
            overflowMenu.Font = UiFonts.Caption;
            ToolStripMenuItem overflowBackItem = new ToolStripMenuItem("后退");
            overflowBackItem.Click += delegate { NavigateBack(); };
            ToolStripMenuItem overflowReloadItem = new ToolStripMenuItem("刷新");
            overflowReloadItem.Click += delegate { if (webViewReady) webView.Reload(); };
            overflowMenu.Items.Add(overflowBackItem);
            overflowMenu.Items.Add(overflowReloadItem);

            RoundedButton[] buttons = { backButton, reloadButton, overflowButton, pinButton, muteButton, settingsButton, chromeButton, hideButton, closeButton };
            foreach (RoundedButton button in buttons) toolbar.Controls.Add(button);
            toolbar.Controls.Add(statusDot);

            webView.Dock = DockStyle.Fill;
            webView.DefaultBackgroundColor = Color.FromArgb(16, 16, 16);
            contentHost.Controls.Add(webView);
            BuildSettingsPanel();
            contentHost.Controls.Add(settingsPanel);
            pageNotice.Visible = false;
            pageNotice.AccessibleName = "页面状态与恢复";
            pageNotice.Click += delegate
            {
                if (!string.IsNullOrEmpty(transientNotice)) { ClearTransientNotice(); return; }
                if (navigationFailed) { if (webViewReady) webView.Reload(); }
                else ReapplyPageStyles();
            };
            contentHost.Controls.Add(pageNotice);
            settingsPanel.BringToFront();
            Controls.Add(contentHost);
            Controls.Add(toolbar);

            pointerTimer.Interval = 10;
            pointerTimer.Tick += MonitorPointer;
            liveTimer.Interval = 250;
            liveTimer.Tick += async delegate
            {
                Diagnostics.Mark("liveTimer tick");
                await DetectLiveStateAsync();
            };
            settingsSaveTimer.Interval = 350;
            settingsSaveTimer.Tick += delegate { settingsSaveTimer.Stop(); SaveSettingsNow(); };
            suspendTimer.Interval = WebViewSuspendDelayMilliseconds;
            suspendTimer.Tick += async delegate
            {
                suspendTimer.Stop();
                await SuspendWebViewIfHiddenAsync();
            };
            noticeTimer.Interval = 7000;
            noticeTimer.Tick += delegate { ClearTransientNotice(); };

            Shown += async delegate
            {
                if (!IsTestMode) { InitializeRecoveryTray(); RegisterAllHotkeys(); }
                else windowHotkeyAvailable = chromeHotkeyAvailable = muteHotkeyAvailable = immersiveHotkeyAvailable = true;
                ApplyChromeState(settings.ChromeHidden);
                showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
                pointerTimer.Start();
                SubscribeSessionEvents();
                await InitializeWebViewAsync();
                if (!string.IsNullOrEmpty(updateHealthToken))
                {
                    if (webViewReady) UpdateService.MarkHealthy(updateHealthToken);
                    else
                    {
                        Diagnostics.Log("update-health failed: WebView2 did not initialize");
                        BeginInvoke((MethodInvoker)delegate { QuitApplication(); });
                        return;
                    }
                }
                if (!IsTestMode && !domProbe)
                    await CheckForUpdatesAsync(false);
            };
            Resize += delegate { LayoutWindow(); UpdateZoom(); };
            ResizeEnd += delegate { RememberCurrentBounds(); };
            Move += delegate { RememberCurrentBounds(); };
            FormClosing += OnFormClosing;
            FormClosed += delegate
            {
                UnregisterAllHotkeys();
                UnsubscribeSessionEvents();
                lifecycle.Close();
                DisposeRecoveryTray();
                updateService.Dispose();
                pointerTimer.Dispose(); liveTimer.Dispose(); suspendTimer.Dispose(); settingsSaveTimer.Dispose(); noticeTimer.Dispose();
                toolTip.Dispose();
            };
            KeyDown += CaptureShortcutKey;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshUiScale();
            LayoutWindow();
            UpdateSettingsUi();
            LayoutSettingsPanel();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            dwmRoundsCorners = TryApplyDwmRoundedCorners();
            windowShapeProbed = true;
            shapedSize = Size.Empty;
            ApplyWindowShape();
        }

        private bool TryApplyDwmRoundedCorners()
        {
            try
            {
                int preference = NativeMethods.DWMWCP_ROUND;
                return NativeMethods.DwmSetWindowAttribute(Handle,
                    NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0;
            }
            catch (EntryPointNotFoundException) { return false; }
            catch (DllNotFoundException) { return false; }
        }

        private void ApplyWindowShape()
        {
            if (!IsHandleCreated || !windowShapeProbed) return;
            if (dwmRoundsCorners)
            {
                ClearWindowRegion();
                return;
            }
            Size size = ClientSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            if (size == shapedSize && Math.Abs(uiScale - shapedScale) < 0.001F) return;
            shapedSize = size;
            shapedScale = uiScale;

            int radius = Scaled(WindowCornerRadius);
            Region next;
            using (GraphicsPath path = UiPaint.RoundedPath(new RectangleF(0, 0, size.Width, size.Height), radius))
                next = new Region(path);
            Region previous = windowRegion;
            windowRegion = next;
            Region = next;
            if (previous != null) previous.Dispose();
        }

        private void ClearWindowRegion()
        {
            if (windowRegion == null && Region == null) return;
            if (windowRegion != null)
            {
                windowRegion.Dispose();
                windowRegion = null;
            }
            Region = null;
            shapedSize = Size.Empty;
        }

        private void RefreshUiScale()
        {
            float scale = 1F;
            try
            {
                using (Graphics graphics = CreateGraphics()) scale = graphics.DpiX / 96F;
            }
            catch
            {
                scale = 1F;
            }
            if (scale < 1F) scale = 1F;
            if (scale > 3F) scale = 3F;
            uiScale = scale;
        }

        private int Scaled(int designPixels)
        {
            return (int)Math.Round(designPixels * uiScale);
        }

        private static void ObserveTask(Task task, string context)
        {
            if (task == null) return;
            task.ContinueWith(delegate(Task failed)
            {
                Exception exception = failed.Exception == null ? null : failed.Exception.GetBaseException();
                Diagnostics.LogException(context, exception);
            }, System.Threading.CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                Diagnostics.Log("hotkey id=" + id);
                Diagnostics.Mark("hotkey " + id);
                if (id == HotkeyWindow) ToggleWindow();
                else if (id == HotkeyChrome) ToggleChrome();
                else if (id == HotkeyMute) ToggleMute();
                else if (id == HotkeyImmersive) ToggleImmersiveMode();
                return;
            }

            if (m.Msg == NativeMethods.WM_ACTIVATEAPP && m.WParam == IntPtr.Zero)
            {
                if (!isQuitting && !IsDisposed && IsHandleCreated)
                    BeginInvoke((MethodInvoker)HideWhenApplicationBecomesInactive);
            }

            if (m.Msg == NativeMethods.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if (m.Result.ToInt32() == 1)
                {
                    Point cursor = PointToClient(Cursor.Position);
                    int grip = 7;
                    bool left = cursor.X <= grip;
                    bool right = cursor.X >= ClientSize.Width - grip;
                    bool top = cursor.Y <= grip;
                    bool bottom = cursor.Y >= ClientSize.Height - grip;
                    if (left && top) m.Result = (IntPtr)NativeMethods.HTTOPLEFT;
                    else if (right && top) m.Result = (IntPtr)NativeMethods.HTTOPRIGHT;
                    else if (left && bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT;
                    else if (right && bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)NativeMethods.HTLEFT;
                    else if (right) m.Result = (IntPtr)NativeMethods.HTRIGHT;
                    else if (top) m.Result = (IntPtr)NativeMethods.HTTOP;
                    else if (bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }

        internal void ShowFromSecondInstance()
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate { ShowWindow(); });
        }

        private void ToggleMute()
        {
            settings.Muted = !settings.Muted;
            ApplyMediaState();
            ScheduleSettingsSave();
            UpdateToolbarState();
        }

        private void NavigateBack()
        {
            if (settingsOpen)
            {
                Diagnostics.Log("NavigateBack close-settings");
                ToggleSettings();
                return;
            }
            if (webView.CanGoBack) webView.GoBack();
        }

        private void ToggleSettings()
        {
            Diagnostics.Log("ToggleSettings open=" + (!settingsOpen) + " chromeHidden=" + settings.ChromeHidden);
            if (settings.ChromeHidden) return;
            if (captureTarget != null) CancelShortcutCapture();
            settingsOpen = !settingsOpen;
            webView.Visible = !settingsOpen;
            settingsPanel.Visible = settingsOpen;
            if (settingsOpen) settingsPanel.BringToFront();
            else webView.BringToFront();
            // Entering immersive mode from Settings changes the content-host height. Keep those DOM
            // changes deferred while WebView2 is hidden, then apply them only after its compositor is
            // visible again. Resizing/injecting into the hidden view can leave the player black.
            LayoutWindow();
            if (!settingsOpen) ApplyPageElementVisibility();
            outsideSince = null;
            UpdateToolbarState();
        }

        private void ToggleChrome()
        {
            if (!settings.ChromeHidden && !chromeHotkeyAvailable)
            {
                ShowTransientNotice("边框恢复快捷键不可用，不能进入无边框模式。");
                return;
            }
            ApplyChromeState(!settings.ChromeHidden);
            ScheduleSettingsSave();
        }

        private void ApplyChromeState(bool hidden)
        {
            Diagnostics.Log("ApplyChromeState hidden=" + hidden);
            if (hidden && captureTarget != null) CancelShortcutCapture();
            settings.ChromeHidden = hidden;
            if (hidden)
            {
                settingsOpen = false;
                settingsPanel.Visible = false;
                webView.Visible = true;
            }
            toolbar.Visible = !hidden;
            Padding = hidden ? Padding.Empty : new Padding(1, 1, 1, 1);
            LayoutWindow();
            UpdateToolbarState();
        }

        private void UpdateToolbarState()
        {
            pinButton.Active = TopMost;
            muteButton.Active = settings.Muted;
            settingsButton.Active = settingsOpen;
            hideButton.Enabled = CanRestoreWindow || !IsHandleCreated;
            chromeButton.Enabled = chromeHotkeyAvailable || !IsHandleCreated;
            toolTip.SetToolTip(pinButton, TopMost ? "取消置顶" : "始终置顶");
            toolTip.SetToolTip(muteButton, (settings.Muted ? "恢复声音" : "静音") + FormatShortcut(muteHotkey));
            toolTip.SetToolTip(hideButton, "立即隐藏" + FormatShortcut(windowHotkey));
            toolTip.SetToolTip(chromeButton, "隐藏边框" + FormatShortcut(chromeHotkey));
        }

        private void UpdateZoom()
        {
            if (webViewReady) webView.ZoomFactor = WindowRules.CalculateZoomFactor(ClientSize.Width);
        }

        private void LayoutWindow()
        {
            Diagnostics.Mark("LayoutWindow enter");
            int toolbarHeight = ToolbarCollapsed ? 0 : Scaled(ToolbarHeight);
            toolbar.SetBounds(Padding.Left, Padding.Top, Math.Max(1, ClientSize.Width - Padding.Horizontal), toolbarHeight);
            contentHost.SetBounds(Padding.Left, Padding.Top + toolbarHeight,
                Math.Max(1, ClientSize.Width - Padding.Horizontal),
                Math.Max(1, ClientSize.Height - Padding.Vertical - toolbarHeight));
            LayoutToolbarButtons(toolbarHeight);
            settingsPanel.Bounds = contentHost.ClientRectangle;
            pageNotice.SetBounds(Scaled(8), Scaled(8), Math.Max(1, contentHost.Width - Scaled(16)), Scaled(36));
            RefreshPageNotice();
            ApplyWindowShape();
            Diagnostics.Mark("LayoutWindow exit tb=" + toolbarHeight + " client=" + ClientSize);
        }

        private void LayoutToolbarButtons(int toolbarHeight)
        {
            int sidePadding = Scaled(ToolbarSidePadding);
            int buttonWidth = Scaled(ToolbarButtonWidth);
            int buttonHeight = Math.Max(1, Math.Min(Scaled(ToolbarButtonHeight), toolbarHeight));
            int gap = Scaled(ToolbarButtonGap);
            int radius = Scaled(8);
            int right = Math.Max(sidePadding, toolbar.Width - sidePadding);
            int available = right - sidePadding;
            int dotSpace = Scaled(12);

            RoundedButton[] all = { backButton, reloadButton, overflowButton, pinButton, muteButton, settingsButton, chromeButton, hideButton, closeButton };
            foreach (RoundedButton button in all) button.CornerRadius = radius;

            // 窄窗：后退/刷新收纳进「更多」溢出菜单；statusDot 让位，保证顶部留白可拖拽。
            bool fold = 8 * buttonWidth + 7 * gap + dotSpace > available - Scaled(8);
            backButton.Visible = !fold;
            reloadButton.Visible = !fold;
            overflowButton.Visible = fold;
            RoundedButton[] visible = fold
                ? new RoundedButton[] { overflowButton, pinButton, muteButton, settingsButton, chromeButton, hideButton, closeButton }
                : new RoundedButton[] { backButton, reloadButton, pinButton, muteButton, settingsButton, chromeButton, hideButton, closeButton };
            int count = visible.Length;
            statusDot.Visible = !fold && count * buttonWidth + (count - 1) * gap + dotSpace <= available;

            int usable = available - (statusDot.Visible ? dotSpace : 0);
            if (count > 0 && count * buttonWidth + (count - 1) * gap > usable)
            {
                buttonWidth = Math.Max(Scaled(14), (usable - (count - 1) * gap) / count);
                gap = Scaled(1);
                if (count > 1 && count * buttonWidth + (count - 1) * gap > usable)
                    gap = Math.Max(0, (usable - count * buttonWidth) / (count - 1));
            }

            int top = (toolbarHeight - buttonHeight) / 2;
            int x = right;
            for (int i = count - 1; i >= 0; i--)
            {
                x -= buttonWidth;
                visible[i].SetBounds(x, top, buttonWidth, buttonHeight);
                x -= gap;
            }
            int dotSize = Scaled(7);
            statusDot.Size = new Size(dotSize, dotSize);
            statusDot.Location = new Point(sidePadding + Scaled(4), (toolbarHeight - dotSize) / 2);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
        }

        private void ShowOverflowMenu()
        {
            overflowMenu.Show(overflowButton, new Point(0, overflowButton.Height));
        }

        private void ScheduleSettingsSave()
        {
            if (IsTestMode || domProbe) return;
            settingsSaveTimer.Stop();
            settingsSaveTimer.Start();
        }

        private void SaveSettingsNow()
        {
            if (IsTestMode || domProbe) return;
            settings.AlwaysOnTop = TopMost;
            settings.HideShortcut = windowHotkey == null ? "" : windowHotkey.Serialize();
            settings.ChromeShortcut = chromeHotkey == null ? "" : chromeHotkey.Serialize();
            settings.MuteShortcut = muteHotkey == null ? "" : muteHotkey.Serialize();
            settings.ImmersiveShortcut = immersiveHotkey == null ? "" : immersiveHotkey.Serialize();
            try { settingsStore.Save(settings); }
            catch (Exception exception)
            {
                statusDot.State = StatusDotState.Error;
                Diagnostics.LogException("SaveSettings", exception);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (isQuitting || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                isQuitting = true;
                lifecycle.Close();
                SaveSettingsNow();
                return;
            }
            e.Cancel = true;
            HideWindow();
        }

        private void QuitApplication()
        {
            isQuitting = true;
            lifecycle.Close();
            pointerTimer.Stop();
            liveTimer.Stop();
            suspendTimer.Stop();
            settingsSaveTimer.Stop();
            SaveSettingsNow();
            Close();
        }

        private RoundedButton CreateToolbarButton(string glyph, string tooltip, Action action)
        {
            RoundedButton button = new RoundedButton();
            button.Text = glyph;
            button.Font = new Font("Segoe MDL2 Assets", 10F);
            button.ForeColor = UiPalette.ToolbarGlyph;
            button.BackColor = UiPalette.Toolbar;
            button.FaceColor = Color.Transparent;
            button.HoverFaceColor = UiPalette.ToolbarHover;
            button.PressedFaceColor = UiPalette.ToolbarPressed;
            button.ActiveFaceColor = UiPalette.ToolbarActive;
            button.ActiveForeColor = UiPalette.Accent;
            button.TabStop = false;
            button.Cursor = Cursors.Hand;
            button.AccessibleName = tooltip;
            button.Click += delegate { action(); };
            toolTip.SetToolTip(button, tooltip);
            return button;
        }

        private static RoundedButton CreateShortcutButton()
        {
            RoundedButton button = new RoundedButton();
            button.FaceColor = UiPalette.ShortcutFace;
            button.ForeColor = UiPalette.TextPrimary;
            button.Font = UiFonts.Caption;
            button.Cursor = Cursors.Hand;
            button.AccessibleRole = AccessibleRole.PushButton;
            return button;
        }

        private static Label CreateStatusLabel()
        {
            Label label = new Label();
            label.Text = "";
            label.Font = UiFonts.Micro;
            label.ForeColor = UiPalette.TextSecondary;
            label.BackColor = UiPalette.Card;
            label.AutoSize = true;
            return label;
        }

        private const string LiveSmokeTestHtml = @"<!doctype html><html><body style=""margin:0;background:#101010;color:white"">
          <div id=""HeaderLayout"" style=""height:50px;background:red"">header</div>
          <div id=""ContainerBackgroundLayout"" style=""display:flex;width:50%;height:200px;background:#222"">
            <div id=""LeftBackgroundLayout""><div id=""PlayerLayout""><div class=""__livingPlayer__"" style=""background:#15803d"">video</div></div></div>
            <div id=""RightPanelLayout"" style=""width:180px;background:blue"">chat</div>
          </div>
          <div id=""GiftMenuLayout"" style=""height:80px;background:red"">gift</div>
          <div id=""BottomLayout"" style=""height:88px;background:red"">gift shortcuts</div>
        </body></html>";

    }

}
