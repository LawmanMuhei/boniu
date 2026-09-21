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
    internal sealed class MainForm : Form
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

        private readonly WebView2 webView = new WebView2();
        private readonly Panel toolbar = new Panel();
        private readonly Panel contentHost = new Panel();
        private readonly DarkScrollPanel settingsPanel = new DarkScrollPanel();
        private readonly StatusDot statusDot = new StatusDot();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Timer pointerTimer = new Timer();
        private readonly Timer liveTimer = new Timer();
        private readonly Timer settingsSaveTimer = new Timer();
        private readonly Timer suspendTimer = new Timer();
        private readonly RoundedButton pinButton;
        private readonly RoundedButton muteButton;
        private readonly RoundedButton settingsButton;
        private readonly RoundedButton chromeButton;
        private readonly RoundedButton hideButton;
        private readonly RoundedButton windowShortcutButton = CreateShortcutButton();
        private readonly RoundedButton chromeShortcutButton = CreateShortcutButton();
        private readonly RoundedButton immersiveShortcutButton = CreateShortcutButton();
        private readonly Label headingLabel = new Label();
        private readonly Label brandLabel = new Label();
        private readonly Label windowShortcutStatus = CreateStatusLabel();
        private readonly Label chromeShortcutStatus = CreateStatusLabel();
        private readonly Label immersiveShortcutStatus = CreateStatusLabel();
        private readonly Label autoHideStatus = CreateStatusLabel();
        private readonly Label inactiveHideStatus = CreateStatusLabel();
        private readonly Label liveStatus = CreateStatusLabel();
        private readonly Label dataStatus = CreateStatusLabel();
        private readonly Label updateStatus = CreateStatusLabel();
        private readonly ToggleSwitch autoHideToggle = new ToggleSwitch();
        private readonly ToggleSwitch inactiveHideToggle = new ToggleSwitch();
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
        private readonly HotkeyDefinition muteHotkey;
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
        private bool suppressInactiveHide;
        private bool sessionEventsSubscribed;
        private bool updateCheckRunning;
        private string captureTarget;
        private DateTime? outsideSince;
        private DateTime showGraceUntil;

        internal MainForm(bool smokeTest, bool liveSmokeTest, bool settingsSmokeTest, bool domProbe,
            string updateHealthToken = null)
        {
            this.smokeTest = smokeTest;
            this.liveSmokeTest = liveSmokeTest;
            this.settingsSmokeTest = settingsSmokeTest;
            this.domProbe = domProbe;
            this.updateHealthToken = updateHealthToken;
            appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniViewWebView2");
            userDataFolder = smokeTest || liveSmokeTest || settingsSmokeTest
                ? Path.Combine(Path.GetTempPath(), "MiniViewWebView2Smoke", Process.GetCurrentProcess().Id.ToString())
                : Path.Combine(appFolder, "UserData");
            settingsStore = new SettingsStore(Path.Combine(appFolder, "settings.json"));
            updateService = new UpdateService(appFolder);
            settings = smokeTest || liveSmokeTest || settingsSmokeTest ? new AppSettings() : settingsStore.Load();
            if (liveSmokeTest) settings.AutoLandscapeLive = true;

            HotkeyDefinition parsed;
            windowHotkey = HotkeyDefinition.TryParse(settings.HideShortcut, out parsed)
                ? parsed : new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.D);
            chromeHotkey = HotkeyDefinition.TryParse(settings.ChromeShortcut, out parsed)
                ? parsed : new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.B);
            muteHotkey = new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.M);
            immersiveHotkey = HotkeyDefinition.TryParse(settings.ImmersiveShortcut, out parsed)
                ? parsed : new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.F);

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

            RoundedButton backButton = CreateToolbarButton("\uE72B", "后退", NavigateBack);
            RoundedButton reloadButton = CreateToolbarButton("\uE72C", "刷新", delegate { if (webViewReady) webView.Reload(); });
            pinButton = CreateToolbarButton("\uE718", "取消置顶", ToggleAlwaysOnTop);
            muteButton = CreateToolbarButton("\uE767", "静音", ToggleMute);
            settingsButton = CreateToolbarButton("\uE713", "设置", ToggleSettings);
            chromeButton = CreateToolbarButton("\uE8A7", "隐藏边框", ToggleChrome);
            hideButton = CreateToolbarButton("\uE890", "立即隐藏", HideWindowSafely);
            RoundedButton closeButton = CreateToolbarButton("\uE8BB", "退出", QuitApplication);
            closeButton.HoverFaceColor = UiPalette.CloseHover;
            closeButton.PressedFaceColor = UiPalette.ClosePressed;
            closeButton.ActiveForeColor = UiPalette.TextPrimary;

            RoundedButton[] buttons = { backButton, reloadButton, pinButton, muteButton, settingsButton, chromeButton, hideButton, closeButton };
            foreach (RoundedButton button in buttons) toolbar.Controls.Add(button);
            toolbar.Controls.Add(statusDot);

            webView.Dock = DockStyle.Fill;
            webView.DefaultBackgroundColor = Color.FromArgb(16, 16, 16);
            contentHost.Controls.Add(webView);
            BuildSettingsPanel();
            contentHost.Controls.Add(settingsPanel);
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

            Shown += async delegate
            {
                RegisterAllHotkeys();
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
                if (!smokeTest && !liveSmokeTest && !settingsSmokeTest && !domProbe)
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

        private async Task InitializeWebViewAsync()
        {
            try
            {
                statusDot.State = StatusDotState.Loading;
                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(
                    "--autoplay-policy=no-user-gesture-required --disable-features=HardwareMediaKeyHandling");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                await webView.EnsureCoreWebView2Async(environment);

                CoreWebView2 core = webView.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e)
                {
                    e.State = CoreWebView2PermissionState.Deny;
                    e.Handled = true;
                };
                core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                core.NewWindowRequested += OnNewWindowRequested;
                core.NavigationStarting += OnNavigationStarting;
                core.WebMessageReceived += OnWebMessageReceived;
                core.NavigationCompleted += async delegate
                {
                    statusDot.State = StatusDotState.Ready;
                    ApplyPageElementVisibility();
                    await DetectLiveStateAsync();
                };
                core.SourceChanged += async delegate
                {
                    ApplyPageElementVisibility();
                    await DetectLiveStateAsync();
                };
                core.IsMuted = settings.Muted;
                webViewReady = true;
                UpdateZoom();
                liveTimer.Start();

                if (liveSmokeTest)
                {
                    webView.NavigateToString(LiveSmokeTestHtml);
                    Timer exitTimer = new Timer();
                    exitTimer.Interval = 2400;
                    exitTimer.Tick += async delegate
                    {
                        exitTimer.Stop();
                        exitTimer.Dispose();
                        bool overlaysHidden = false;
                        try
                        {
                            string hiddenResult = await webView.ExecuteScriptAsync(@"(() =>
                              ['HeaderLayout', 'GiftMenuLayout', 'BottomLayout', 'RightPanelLayout']
                                .every(id => getComputedStyle(document.getElementById(id)).display === 'none'))()");
                            overlaysHidden = string.Equals(hiddenResult, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                        }
                        Environment.ExitCode = liveLandscapeApplied && overlaysHidden ? 0 : 3;
                        QuitApplication();
                    };
                    exitTimer.Start();
                }
                else if (settingsSmokeTest)
                {
                    webView.NavigateToString("<!doctype html><html><body style='background:#101010;color:white'>settings test</body></html>");
                    settingsOpen = true;
                    webView.Visible = false;
                    settingsPanel.Visible = true;
                    settingsPanel.BringToFront();
                    SetBoundsProgrammatically(new Rectangle(Left, Top, 280, 460));
                    LayoutWindow();
                    UpdateSettingsUi();
                    Timer exitTimer = new Timer();
                    exitTimer.Interval = 1200;
                    exitTimer.Tick += delegate
                    {
                        exitTimer.Stop();
                        exitTimer.Dispose();
                        bool layoutValid = settingsPanel.CanScroll
                            && settingsPanel.ScrollOffsetY == 0
                            && autoHideDelayPicker.Items.Count == 3
                            && autoHideDelayPicker.SelectedIndex == 0
                            && clearDataButton.Bottom <= settingsPanel.ScrollContentHeight
                            && clearDataButton.Width <= settingsPanel.ClientSize.Width
                            && SettingsLayoutIsConsistent()
                            && LayoutSurvivesScrollAndRelayout()
                            && LayoutSurvivesResizeWhileScrolled()
                            && LayoutIsConsistentAcrossScales()
                            && dwmRoundsCorners == (Region == null);
                        if (!layoutValid)
                        {
                            Console.Error.WriteLine("settings layout failed: canScroll={0}, offsetY={1}, items={2}, selected={3}, buttonBottom={4}, contentHeight={5}, buttonWidth={6}, clientWidth={7}",
                                settingsPanel.CanScroll, settingsPanel.ScrollOffsetY,
                                autoHideDelayPicker.Items.Count, autoHideDelayPicker.SelectedIndex,
                                clearDataButton.Bottom, settingsPanel.ScrollContentHeight,
                                clearDataButton.Width, settingsPanel.ClientSize.Width);
                        }
                        Environment.ExitCode = layoutValid ? 0 : 4;
                        QuitApplication();
                    };
                    exitTimer.Start();
                }
                else if (smokeTest)
                {
                    webView.NavigateToString("<!doctype html><html><body style='background:#101010;color:white'>波妞摸鱼 WebView2 OK</body></html>");
                    Timer exitTimer = new Timer();
                    exitTimer.Interval = 1200;
                    exitTimer.Tick += delegate
                    {
                        exitTimer.Stop();
                        exitTimer.Dispose();
                        Environment.ExitCode = 0;
                        QuitApplication();
                    };
                    exitTimer.Start();
                }
                else
                {
                    webView.Source = new Uri(DouyinUrl);
                    if (domProbe)
                    {
                        Timer probeTimer = new Timer();
                        probeTimer.Interval = 9000;
                        probeTimer.Tick += delegate
                        {
                            probeTimer.Stop();
                            probeTimer.Dispose();
                            RunDomProbe();
                        };
                        probeTimer.Start();
                    }
                }
            }
            catch (Exception exception)
            {
                statusDot.State = StatusDotState.Error;
                if (smokeTest || liveSmokeTest || settingsSmokeTest || domProbe)
                {
                    Environment.ExitCode = 2;
                    QuitApplication();
                    return;
                }
                MessageBox.Show(this,
                    "WebView2 初始化失败。请确认 Windows 10/11 已安装 Microsoft Edge WebView2 Runtime。\r\n\r\n" + exception.Message,
                    "波妞摸鱼无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void RunDomProbe()
        {
            string folder = System.IO.Path.GetTempPath();
            try
            {
                string clicked = await webView.ExecuteScriptAsync(OpenRecommendScript);
                Console.Error.WriteLine("open-recommend " + clicked);
                await Task.Delay(6000);
                string baseline = await webView.ExecuteScriptAsync(DomProbeScript);
                System.IO.File.WriteAllText(Path.Combine(folder, "boniu-dom.json"), baseline);
                DumpWindowShot(folder, "boniu-baseline.png");
                Console.Error.WriteLine("dom-probe baseline length=" + baseline.Length);

                settings.ImmersiveMode = true;
                toolbarRevealed = false;
                LayoutWindow();
                ApplyPageElementVisibility();
                await Task.Delay(4000);
                string immersive = await webView.ExecuteScriptAsync(DomProbeScript);
                System.IO.File.WriteAllText(Path.Combine(folder, "boniu-dom-immersive.json"), immersive);
                DumpWindowShot(folder, "boniu-immersive.png");
                Console.Error.WriteLine("dom-probe immersive length=" + immersive.Length);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("dom-probe failed: " + exception.Message);
                Environment.ExitCode = 5;
            }
            QuitApplication();
        }

        private void DumpWindowShot(string folder, string fileName)
        {
            try
            {
                Rectangle bounds = RectangleToScreen(ClientRectangle);
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
                using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(bounds.Location, Point.Empty, bitmap.Size);
                    bitmap.Save(Path.Combine(folder, fileName), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch
            {
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if ((smokeTest || liveSmokeTest)
                && (string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase)
                    || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))) return;
            if (!WindowRules.IsDouyinUrl(e.Uri)) e.Cancel = true;
            else statusDot.State = StatusDotState.Loading;
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (WindowRules.IsDouyinUrl(e.Uri)) webView.Source = new Uri(e.Uri);
        }

        private async Task DetectLiveStateAsync()
        {
            if (!webViewReady || liveDetectionPending || webView.CoreWebView2 == null) return;
            liveDetectionPending = true;
            bool hasLivePlayer = false;
            try
            {
                string result = await webView.ExecuteScriptAsync(LiveProbeScript);
                hasLivePlayer = string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
            }
            finally
            {
                liveDetectionPending = false;
            }

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

        private void MonitorPointer(object sender, EventArgs e)
        {
            Diagnostics.Tick();
            Diagnostics.Mark("pointer");
            UpdateImmersiveToolbar();
            if (smokeTest || liveSmokeTest || settingsSmokeTest || domProbe || !settings.AutoHideEnabled || !windowHotkeyAvailable
                || captureTarget != null || !Visible
                || DateTime.UtcNow < showGraceUntil) return;
            NativeMethods.NativeRectangle nativeBounds;
            bool hasNativeBounds = NativeMethods.GetWindowRect(Handle, out nativeBounds);
            Point cursor = Cursor.Position;
            bool inside = hasNativeBounds
                ? cursor.X >= nativeBounds.Left - 7 && cursor.X < nativeBounds.Right + 7
                    && cursor.Y >= nativeBounds.Top - 7 && cursor.Y < nativeBounds.Bottom + 7
                : new Rectangle(Bounds.X - 7, Bounds.Y - 7, Bounds.Width + 14, Bounds.Height + 14).Contains(cursor);
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
            if (!Visible) return;
            Diagnostics.Mark("HideWindow");
            outsideSince = null;
            if (webViewReady)
            {
                try { webView.CoreWebView2.IsMuted = true; }
                catch { }
                PauseVideosAfterHide();
            }
            liveTimer.Stop();
            Hide();
            if (captureTarget != null) CancelShortcutCapture();
            if (settingsOpen)
            {
                settingsOpen = false;
                settingsPanel.Visible = false;
                webView.Visible = true;
                webView.BringToFront();
            }
            ArmSuspendTimer(WebViewSuspendDelayMilliseconds);
        }

        private async void PauseVideosAfterHide()
        {
            try { await webView.ExecuteScriptAsync(PauseVideosScript); }
            catch { }
        }

        private void HideWindowSafely()
        {
            if (windowHotkeyAvailable) HideWindow();
            else MessageBox.Show(this, "显示快捷键不可用，本次运行不能隐藏窗口。", "波妞摸鱼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async void ShowWindow()
        {
            suspendTimer.Stop();
            ResumeSuspendedWebView();
            showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
            outsideSince = null;
            Show();
            Activate();
            LayoutWindow();
            if (webViewReady)
            {
                webView.CoreWebView2.IsMuted = settings.Muted;
                try { await webView.ExecuteScriptAsync(ResumeVideosScript); }
                catch { }
                liveTimer.Start();
            }
        }

        private async Task SuspendWebViewIfHiddenAsync()
        {
            if (Visible || webViewSuspended || webViewSuspendPending) return;
            if (!webViewReady || webView.CoreWebView2 == null)
            {
                ArmSuspendTimer(2000);
                return;
            }
            bool retry = false;
            webViewSuspendPending = true;
            try
            {
                bool suspended = await webView.CoreWebView2.TrySuspendAsync();
                if (suspended)
                {
                    if (Visible) webView.CoreWebView2.Resume();
                    else webViewSuspended = true;
                    Diagnostics.Log("webview-suspend success hidden=" + (!Visible));
                }
                else
                {
                    Diagnostics.Log("webview-suspend declined");
                    retry = !Visible;
                }
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("SuspendWebView", exception);
                retry = !Visible;
            }
            finally
            {
                webViewSuspendPending = false;
                if (retry && !Visible) ArmSuspendTimer(5000);
            }
        }

        private void ArmSuspendTimer(int delayMilliseconds)
        {
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
                Diagnostics.Log("webview-resume");
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("ResumeWebView", exception);
            }
            finally
            {
                webViewSuspended = false;
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (message != "boniu:immersive-fallback") return;
            Diagnostics.Log("immersive-health fallback-to-native-layout");
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                immersiveStatus.Text = "页面结构变化，已安全回退原始布局";
                LayoutSettingsPanel();
            });
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
            if (!settings.HideWhenInactive || suppressInactiveHide || !windowHotkeyAvailable
                || captureTarget != null || !Visible || isQuitting) return;
            HideWindow();
        }

        private void SubscribeSessionEvents()
        {
            if (smokeTest || liveSmokeTest || settingsSmokeTest || sessionEventsSubscribed) return;
            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                sessionEventsSubscribed = true;
            }
            catch
            {
            }
        }

        private void UnsubscribeSessionEvents()
        {
            if (!sessionEventsSubscribed) return;
            try { SystemEvents.SessionSwitch -= OnSessionSwitch; }
            catch { }
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
                if (Visible && windowHotkeyAvailable) HideWindow();
            });
        }

        private void ToggleMute()
        {
            settings.Muted = !settings.Muted;
            if (webViewReady) webView.CoreWebView2.IsMuted = Visible ? settings.Muted : true;
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
                MessageBox.Show(this, "边框恢复快捷键不可用，不能进入无边框模式。", "波妞摸鱼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private void BuildSettingsPanel()
        {
            settingsPanel.BackColor = UiPalette.Page;
            settingsPanel.Visible = false;

            headingLabel.Text = "设置";
            headingLabel.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
            headingLabel.ForeColor = UiPalette.TextPrimary;
            headingLabel.BackColor = UiPalette.Page;
            headingLabel.AutoSize = true;
            settingsPanel.Controls.Add(headingLabel);

            brandLabel.Text = "波妞摸鱼";
            brandLabel.Font = new Font("Microsoft YaHei UI", 7F);
            brandLabel.ForeColor = UiPalette.TextTertiary;
            brandLabel.BackColor = UiPalette.Page;
            brandLabel.AutoSize = true;
            settingsPanel.Controls.Add(brandLabel);

            SettingsCard hotkeyCard = AddCard("快捷键");
            AddInlineRow(hotkeyCard, "隐藏/显示程序", windowShortcutStatus, windowShortcutButton, 104, 30);
            AddInlineRow(hotkeyCard, "隐藏/显示边框", chromeShortcutStatus, chromeShortcutButton, 104, 30);
            AddInlineRow(hotkeyCard, "进入/退出清爽模式", immersiveShortcutStatus, immersiveShortcutButton, 104, 30);

            SettingsCard behaviorCard = AddCard("行为");
            AddInlineRow(behaviorCard, "鼠标移出自动隐藏", autoHideStatus, autoHideToggle, 44, 26);
            delayRow = AddStackedRow(behaviorCard, "隐藏延迟", autoHideDelayPicker, 28);
            AddInlineRow(behaviorCard, "失去焦点时隐藏", inactiveHideStatus, inactiveHideToggle, 44, 26);
            AddInlineRow(behaviorCard, "直播自动横屏", liveStatus, landscapeToggle, 44, 26);

            SettingsCard interfaceCard = AddCard("界面");
            AddInlineRow(interfaceCard, "隐藏左侧导航栏", leftNavStatus, leftNavToggle, 44, 26);
            AddInlineRow(interfaceCard, "隐藏顶部搜索栏", topBarStatus, topBarToggle, 44, 26);
            AddInlineRow(interfaceCard, "隐藏右侧互动区", rightBarStatus, rightBarToggle, 44, 26);
            AddInlineRow(interfaceCard, "清爽模式（只剩视频）", immersiveStatus, immersiveToggle, 44, 26);

            SettingsCard dataCard = AddCard("数据");
            AddInlineRow(dataCard, "本地数据", dataStatus, null, 0, 0);
            AddFullWidthRow(dataCard, openDataButton, 36);
            AddFullWidthRow(dataCard, resetSettingsButton, 36);
            AddFullWidthRow(dataCard, clearDataButton, 36);
            SetDataStatus("登录和设置仅保存在本机");

            SettingsCard updateCard = AddCard("关于与更新");
            AddInlineRow(updateCard, "当前版本", updateStatus, null, 0, 0);
            AddFullWidthRow(updateCard, checkUpdateButton, 36);
            updateStatus.Text = "v" + UpdateService.CurrentVersion.ToString(3) + " · 启动时检查更新";

            autoHideDelayPicker.Font = new Font("Microsoft YaHei UI", 8F);
            autoHideDelayPicker.AccessibleName = "隐藏延迟";
            autoHideDelayPicker.Items.Add("0.1 秒");
            autoHideDelayPicker.Items.Add("0.3 秒");
            autoHideDelayPicker.Items.Add("0.5 秒");

            windowShortcutButton.Click += delegate { BeginShortcutCapture("window"); };
            chromeShortcutButton.Click += delegate { BeginShortcutCapture("chrome"); };
            autoHideToggle.CheckedChanged += delegate
            {
                if (autoHideToggle.Checked == settings.AutoHideEnabled) return;
                settings.AutoHideEnabled = autoHideToggle.Checked;
                outsideSince = null;
                ScheduleSettingsSave();
                UpdateSettingsUi();
            };
            autoHideDelayPicker.SelectedIndexChanged += delegate
            {
                int[] delays = { 100, 300, 500 };
                if (autoHideDelayPicker.SelectedIndex < 0 || autoHideDelayPicker.SelectedIndex >= delays.Length) return;
                int selectedDelay = delays[autoHideDelayPicker.SelectedIndex];
                if (selectedDelay == settings.AutoHideDelayMilliseconds) return;
                settings.AutoHideDelayMilliseconds = selectedDelay;
                outsideSince = null;
                ScheduleSettingsSave();
                UpdateSettingsUi();
            };
            inactiveHideToggle.CheckedChanged += delegate
            {
                if (inactiveHideToggle.Checked == settings.HideWhenInactive) return;
                settings.HideWhenInactive = inactiveHideToggle.Checked;
                ScheduleSettingsSave();
                UpdateSettingsUi();
            };
            landscapeToggle.CheckedChanged += delegate
            {
                if (landscapeToggle.Checked == settings.AutoLandscapeLive) return;
                settings.AutoLandscapeLive = landscapeToggle.Checked;
                if (settings.AutoLandscapeLive) ApplyLiveLayout();
                else RestorePortraitBounds();
                ScheduleSettingsSave();
                UpdateSettingsUi();
            };
            leftNavToggle.CheckedChanged += delegate
            {
                if (leftNavToggle.Checked == settings.HideLeftNav) return;
                settings.HideLeftNav = leftNavToggle.Checked;
                ScheduleSettingsSave();
                UpdateSettingsUi();
                ApplyPageElementVisibility();
            };
            topBarToggle.CheckedChanged += delegate
            {
                if (topBarToggle.Checked == settings.HideTopBar) return;
                settings.HideTopBar = topBarToggle.Checked;
                ScheduleSettingsSave();
                UpdateSettingsUi();
                ApplyPageElementVisibility();
            };
            rightBarToggle.CheckedChanged += delegate
            {
                if (rightBarToggle.Checked == settings.HideRightBar) return;
                settings.HideRightBar = rightBarToggle.Checked;
                ScheduleSettingsSave();
                UpdateSettingsUi();
                ApplyPageElementVisibility();
            };
            immersiveToggle.CheckedChanged += delegate
            {
                if (immersiveToggle.Checked == settings.ImmersiveMode) return;
                settings.ImmersiveMode = immersiveToggle.Checked;
                Diagnostics.Log("immersive=" + settings.ImmersiveMode + " source=settings-switch");
                toolbarRevealed = false;
                ScheduleSettingsSave();
                LayoutWindow();
                UpdateSettingsUi();
                ApplyPageElementVisibility();
            };
            immersiveShortcutButton.Click += delegate { BeginShortcutCapture("immersive"); };
            openDataButton.Click += delegate { OpenLocalDataFolder(); };
            resetSettingsButton.Click += delegate { ResetApplicationSettings(); };
            clearDataButton.Click += async delegate { await ClearLocalBrowsingDataAsync(); };
            checkUpdateButton.Click += async delegate { await CheckForUpdatesAsync(true); };

            settingsPanel.Paint += PaintSettingsCards;
            settingsPanel.Resize += delegate { LayoutSettingsPanel(); };
        }

        private SettingsCard AddCard(string title)
        {
            SettingsCard card = new SettingsCard();
            card.TitleLabel = new Label();
            card.TitleLabel.Text = title;
            card.TitleLabel.Font = new Font("Microsoft YaHei UI", 7F);
            card.TitleLabel.ForeColor = UiPalette.TextTertiary;
            card.TitleLabel.BackColor = UiPalette.Page;
            card.TitleLabel.AutoSize = true;
            settingsPanel.Controls.Add(card.TitleLabel);
            cards.Add(card);
            return card;
        }

        private SettingsRow AddInlineRow(SettingsCard card, string title, Label status, Control editor, int editorWidth, int editorHeight)
        {
            return AddRow(card, SettingsRowKind.Inline, title, status, editor, editorWidth, editorHeight);
        }

        private SettingsRow AddStackedRow(SettingsCard card, string title, Control editor, int editorHeight)
        {
            return AddRow(card, SettingsRowKind.Stacked, title, null, editor, 0, editorHeight);
        }

        private SettingsRow AddFullWidthRow(SettingsCard card, Control editor, int editorHeight)
        {
            return AddRow(card, SettingsRowKind.FullWidth, null, null, editor, 0, editorHeight);
        }

        private SettingsRow AddRow(SettingsCard card, SettingsRowKind kind, string title,
            Label status, Control editor, int editorWidth, int editorHeight)
        {
            SettingsRow row = new SettingsRow();
            row.Kind = kind;
            row.Status = status;
            row.Editor = editor;
            row.EditorWidth = editorWidth;
            row.EditorHeight = editorHeight;
            if (title != null)
            {
                row.TitleLabel = new Label();
                row.TitleLabel.Text = title;
                row.TitleLabel.Font = new Font("Microsoft YaHei UI", 9F);
                row.TitleLabel.ForeColor = UiPalette.TextPrimary;
                row.TitleLabel.BackColor = UiPalette.Card;
                row.TitleLabel.AutoSize = true;
                settingsPanel.Controls.Add(row.TitleLabel);
            }
            if (status != null) settingsPanel.Controls.Add(status);
            if (editor != null) settingsPanel.Controls.Add(editor);
            card.Rows.Add(row);
            return row;
        }

        private static int DesignRowHeight(SettingsRow row)
        {
            if (row.Kind == SettingsRowKind.Stacked) return row.EditorHeight + 32;
            if (row.Kind == SettingsRowKind.FullWidth) return row.EditorHeight + 10;
            return row.Status != null ? 50 : 42;
        }

        private bool layoutSettingsRunning;
        private bool layoutSettingsRequested;
        private int layoutSettingsReentrant;

        private void LayoutSettingsPanel()
        {
            // SetScrollOffset() and AutoScrollMinSize writes re-enter through the panel's Resize
            // subscription. Coalesce those into extra passes with a hard cap so the layout both
            // converges and always ends on the newest size.
            if (layoutSettingsRunning)
            {
                layoutSettingsRequested = true;
                layoutSettingsReentrant++;
                return;
            }

            layoutSettingsRunning = true;
            try
            {
                int passes = 0;
                do
                {
                    layoutSettingsRequested = false;
                    LayoutSettingsPanelCore();
                    passes++;
                }
                while (layoutSettingsRequested && passes < 4);

                if (layoutSettingsRequested)
                    Diagnostics.Log("LayoutSettingsPanel hit pass cap reentrant=" + layoutSettingsReentrant);
            }
            finally
            {
                layoutSettingsRunning = false;
            }
        }

        private void LayoutSettingsPanelCore()
        {
            // Child Locations are written in document space. While the panel is scrolled, WinForms
            // bakes the current offset into them and permanently shifts rows out of their cards.
            int restoreScroll = settingsPanel.ScrollOffsetY;
            if (restoreScroll != 0) settingsPanel.SetScrollOffset(0);

            cardRadius = Math.Max(6, Scaled(12));
            int margin = Scaled(16);
            int cardWidth = Math.Max(1, settingsPanel.ClientSize.Width - margin * 2);
            rowInset = Math.Min(Scaled(20), Math.Max(8, cardWidth / 10));
            int cursor = Scaled(18);

            headingLabel.Location = new Point(margin + Scaled(2), cursor);
            brandLabel.Location = new Point(Math.Max(margin, margin + cardWidth - brandLabel.Width), cursor + Scaled(9));
            cursor += Scaled(44);

            foreach (SettingsCard card in cards)
            {
                card.RowRects.Clear();
                card.Bounds = Rectangle.Empty;
                card.TitleLabel.Visible = false;

                bool anyVisible = false;
                foreach (SettingsRow row in card.Rows)
                    if (!row.Hidden) { anyVisible = true; break; }
                if (!anyVisible) continue;

                card.TitleLabel.Visible = true;
                card.TitleLabel.Location = new Point(margin + Scaled(2), cursor);
                cursor += Scaled(20);

                int top = cursor;
                int rowCursor = 0;
                foreach (SettingsRow row in card.Rows)
                {
                    if (row.TitleLabel != null) row.TitleLabel.Visible = !row.Hidden;
                    if (row.Status != null) row.Status.Visible = !row.Hidden;
                    if (row.Editor != null) row.Editor.Visible = !row.Hidden;
                    if (row.Hidden) continue;

                    Rectangle rowRect = new Rectangle(margin, top + rowCursor, cardWidth, Scaled(DesignRowHeight(row)));
                    card.RowRects.Add(rowRect);
                    PlaceRow(row, rowRect);
                    rowCursor += rowRect.Height;
                }
                card.Bounds = new Rectangle(margin, top, cardWidth, rowCursor);
                cursor = top + rowCursor + Scaled(24);
            }

            settingsPanel.ScrollContentHeight = cursor;
            if (restoreScroll != 0) settingsPanel.SetScrollOffset(restoreScroll);
            settingsPanel.Invalidate();
            Diagnostics.Mark("LayoutSettingsPanel done doc=" + cursor);
        }

        private void PlaceRow(SettingsRow row, Rectangle rowRect)
        {
            int contentLeft = rowRect.X + rowInset;
            int contentWidth = Math.Max(1, rowRect.Width - rowInset * 2);

            if (row.Kind == SettingsRowKind.FullWidth)
            {
                int buttonHeight = Scaled(row.EditorHeight);
                row.Editor.SetBounds(contentLeft, rowRect.Y + (rowRect.Height - buttonHeight) / 2, contentWidth, buttonHeight);
                return;
            }

            int editorWidth = 0;
            int editorHeight = Scaled(row.EditorHeight);
            if (row.Kind == SettingsRowKind.Stacked) editorWidth = contentWidth;
            else if (row.Editor != null)
            {
                int cap = (int)(contentWidth * 0.46);
                editorWidth = row.EditorWidth > 0 ? Scaled(row.EditorWidth) : Scaled(44);
                if (editorWidth > cap)
                {
                    editorWidth = cap;
                    if (row.EditorWidth > 0)
                        editorHeight = Math.Max(1, editorWidth * row.EditorHeight / row.EditorWidth);
                }
            }

            int textLimit = contentWidth;
            if (row.Kind == SettingsRowKind.Inline && row.Editor != null)
                textLimit = Math.Max(1, contentWidth - editorWidth - Scaled(12));

            if (row.TitleLabel != null)
            {
                int titleTop = row.Kind == SettingsRowKind.Stacked ? rowRect.Y + Scaled(6)
                    : row.Status != null ? rowRect.Y + Scaled(8)
                    : rowRect.Y + (rowRect.Height - row.TitleLabel.Height) / 2;
                row.TitleLabel.Location = new Point(contentLeft, titleTop);
                FitLabel(row.TitleLabel, textLimit);
            }

            if (row.Status != null)
            {
                int statusTop = row.TitleLabel != null ? row.TitleLabel.Bottom + Scaled(3)
                    : rowRect.Y + (rowRect.Height - row.Status.Height) / 2;
                row.Status.Location = new Point(contentLeft, statusTop);
                FitLabel(row.Status, textLimit);
            }

            if (row.Editor != null)
            {
                RoundedButton roundedEditor = row.Editor as RoundedButton;
                if (roundedEditor != null) roundedEditor.CornerRadius = Scaled(8);
                int editorLeft = row.Kind == SettingsRowKind.Stacked ? contentLeft : rowRect.Right - rowInset - editorWidth;
                int editorTop = row.Kind == SettingsRowKind.Stacked
                    ? row.TitleLabel.Bottom + Scaled(6)
                    : rowRect.Y + (rowRect.Height - editorHeight) / 2;
                row.Editor.SetBounds(editorLeft, editorTop, editorWidth, editorHeight);
            }
        }

        private static void FitLabel(Label label, int maxWidth)
        {
            if (!label.AutoSize)
            {
                label.AutoSize = true;
                label.AutoEllipsis = false;
            }
            if (maxWidth <= 0 || label.Width <= maxWidth) return;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Width = maxWidth;
        }

        private void SetDataStatus(string message)
        {
            dataStatus.Text = message;
            LayoutSettingsPanel();
        }

        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (updateCheckRunning) return;
            updateCheckRunning = true;
            updateStatus.Text = "正在检查更新…";
            LayoutSettingsPanel();
            try
            {
                UpdateInfo info = await updateService.CheckAsync();
                if (info == null)
                {
                    updateStatus.Text = "v" + UpdateService.CurrentVersion.ToString(3) + " · 已是最新版";
                    if (manual) ShowOwnedMessage("当前已是最新版本。", "检查更新", MessageBoxIcon.Information);
                    return;
                }
                updateStatus.Text = "发现 v" + info.Version.ToString(3);
                string notes = string.IsNullOrWhiteSpace(info.Notes) ? "此版本没有附加更新说明。" : info.Notes.Trim();
                if (notes.Length > 1200) notes = notes.Substring(0, 1200) + "…";
                if (!ShowUpdateConfirmation("发现新版本 v" + info.Version.ToString(3) + "。\r\n\r\n" + notes
                    + "\r\n\r\n是否现在下载并安装？")) return;

                updateStatus.Text = "正在下载并校验…";
                LayoutSettingsPanel();
                PreparedUpdate prepared = await updateService.DownloadAsync(info);
                updateStatus.Text = "正在安全安装…";
                LayoutSettingsPanel();
                SaveSettingsNow();
                updateService.BeginInstall(prepared, Process.GetCurrentProcess().Id);
                QuitApplication();
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("CheckForUpdates", exception);
                updateStatus.Text = "检查或安装失败";
                if (manual) ShowOwnedMessage("无法完成更新。\r\n\r\n" + exception.Message,
                    "检查更新", MessageBoxIcon.Error);
            }
            finally
            {
                updateCheckRunning = false;
                if (!IsDisposed) LayoutSettingsPanel();
            }
        }

        private bool ShowUpdateConfirmation(string message)
        {
            suppressInactiveHide = true;
            try
            {
                return MessageBox.Show(this, message, "波妞摸鱼更新", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            finally
            {
                suppressInactiveHide = false;
                showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
            }
        }

        private void PaintSettingsCards(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int scrollY = settingsPanel.ScrollOffsetY;
            foreach (SettingsCard card in cards)
            {
                if (card.Bounds.IsEmpty) continue;
                Rectangle visible = new Rectangle(card.Bounds.X, card.Bounds.Y - scrollY, card.Bounds.Width, card.Bounds.Height);
                using (GraphicsPath path = UiPaint.RoundedPath(
                    new RectangleF(visible.X + 0.5F, visible.Y + 0.5F, visible.Width - 1F, visible.Height - 1F), cardRadius))
                using (SolidBrush brush = new SolidBrush(UiPalette.Card))
                    e.Graphics.FillPath(brush, path);

                using (Pen pen = new Pen(UiPalette.Separator))
                {
                    for (int i = 0; i < card.RowRects.Count - 1; i++)
                    {
                        int y = card.RowRects[i].Bottom - scrollY;
                        e.Graphics.DrawLine(pen, card.Bounds.X + rowInset, y, card.Bounds.Right - rowInset, y);
                    }
                }
            }
        }

        private bool LayoutSurvivesResizeWhileScrolled()
        {
            int bottom = Math.Max(1, settingsPanel.ScrollContentHeight - settingsPanel.ClientSize.Height);
            Rectangle bounds = settingsPanel.Bounds;
            settingsPanel.SetScrollOffset(bottom);
            layoutSettingsReentrant = 0;
            settingsPanel.SetBounds(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height - 1));
            settingsPanel.SetScrollOffset(bottom);
            // The real crash path: settings closed first, then the toolbar collapse resizes the
            // hidden-but-still-scrolled panel.
            settingsPanel.Visible = false;
            settingsPanel.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            settingsPanel.SetScrollOffset(bottom);
            settingsPanel.SetBounds(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height - 2));
            settingsPanel.Visible = true;
            settingsPanel.SetScrollOffset(bottom);
            settingsPanel.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            int reentrant = layoutSettingsReentrant;
            settingsPanel.SetScrollOffset(0);
            LayoutSettingsPanel();
            Console.Error.WriteLine("resize-while-scrolled reentrant=" + reentrant);
            return reentrant > 0 && SettingsLayoutIsConsistent();
        }

        private bool LayoutSurvivesScrollAndRelayout()
        {
            int bottom = Math.Max(1, settingsPanel.ScrollContentHeight - settingsPanel.ClientSize.Height);
            settingsPanel.SetScrollOffset(bottom);
            LayoutSettingsPanel();
            settingsPanel.SetScrollOffset(0);
            bool valid = settingsPanel.ScrollOffsetY == 0 && SettingsLayoutIsConsistent();
            LayoutSettingsPanel();
            return valid;
        }

        private bool LayoutIsConsistentAcrossScales()
        {
            float originalScale = uiScale;
            bool valid = true;
            foreach (float scale in new float[] { 1F, 1.25F, 1.5F, 2F })
            {
                uiScale = scale;
                LayoutWindow();
                LayoutSettingsPanel();
                if (!SettingsLayoutIsConsistent()) valid = false;
                if (!ToolbarLayoutIsConsistent()) valid = false;
            }
            uiScale = originalScale;
            LayoutWindow();
            LayoutSettingsPanel();
            return valid;
        }

        private bool ToolbarLayoutIsConsistent()
        {
            if (toolbar.Height <= 0) return true;
            Rectangle[] boxes = new Rectangle[toolbar.Controls.Count];
            int count = 0;
            foreach (Control control in toolbar.Controls)
            {
                RoundedButton button = control as RoundedButton;
                if (button == null) continue;
                Rectangle box = button.Bounds;
                if (box.Width <= 0 || box.Height <= 0) return false;
                if (box.Left < 0 || box.Right > toolbar.Width) return false;
                if (box.Top < 0 || box.Bottom > toolbar.Height) return false;
                for (int i = 0; i < count; i++)
                    if (boxes[i].IntersectsWith(box)) return false;
                boxes[count++] = box;
            }
            return statusDot.Left >= 0 && statusDot.Right <= toolbar.Width
                && statusDot.Top >= 0 && statusDot.Bottom <= toolbar.Height;
        }

        private bool SettingsLayoutIsConsistent()
        {
            int previousBottom = int.MinValue;
            foreach (SettingsCard card in cards)
            {
                if (card.Bounds.IsEmpty) continue;
                if (card.Bounds.Top < previousBottom) return false;
                previousBottom = card.Bounds.Bottom;
                if (card.Bounds.Right > settingsPanel.ClientSize.Width - Scaled(8)) return false;
                foreach (SettingsRow row in card.Rows)
                {
                    if (row.Hidden) continue;
                    if (!FitsInsideCard(card, row.TitleLabel)) return false;
                    if (!FitsInsideCard(card, row.Status)) return false;
                    if (!FitsInsideCard(card, row.Editor)) return false;
                }
            }
            return true;
        }

        private static bool FitsInsideCard(SettingsCard card, Control control)
        {
            if (control == null || !control.Visible) return true;
            return control.Left >= card.Bounds.Left && control.Right <= card.Bounds.Right
                && control.Top >= card.Bounds.Top && control.Bottom <= card.Bounds.Bottom;
        }

        private static RoundedButton CreateSettingsActionButton(string text, bool danger)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.FaceColor = UiPalette.ButtonFace;
            if (danger)
            {
                button.HoverFaceColor = Color.FromArgb(74, 40, 40);
                button.PressedFaceColor = Color.FromArgb(96, 46, 46);
            }
            else
            {
                button.HoverFaceColor = Color.FromArgb(58, 58, 60);
                button.PressedFaceColor = Color.FromArgb(72, 72, 76);
            }
            button.ForeColor = danger ? UiPalette.Destructive : UiPalette.TextPrimary;
            button.Font = new Font("Microsoft YaHei UI", 8.5F);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void OpenLocalDataFolder()
        {
            try
            {
                Directory.CreateDirectory(appFolder);
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = appFolder;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
                SetDataStatus("已打开本地数据目录");
            }
            catch (Exception exception)
            {
                ShowOwnedMessage("无法打开本地数据目录。\r\n\r\n" + exception.Message,
                    "波妞摸鱼", MessageBoxIcon.Error);
            }
        }

        private void ResetApplicationSettings()
        {
            if (!ShowConfirmation("确定恢复程序默认设置吗？\r\n\r\n窗口大小、位置、快捷键和显示选项将恢复默认，但不会清除抖音登录。",
                "恢复默认设置")) return;

            UnregisterAllHotkeys();
            settings = new AppSettings();
            HotkeyDefinition parsed;
            HotkeyDefinition.TryParse(settings.HideShortcut, out parsed);
            windowHotkey = parsed;
            HotkeyDefinition.TryParse(settings.ChromeShortcut, out parsed);
            chromeHotkey = parsed;
            HotkeyDefinition.TryParse(settings.ImmersiveShortcut, out parsed);
            immersiveHotkey = parsed;
            TopMost = settings.AlwaysOnTop;
            if (webViewReady) webView.CoreWebView2.IsMuted = settings.Muted;
            liveLandscapeApplied = false;
            MinimumSize = new Size(280, 460);
            ApplyChromeState(false);
            SetBoundsProgrammatically(WindowRules.KeepOnScreen(null, 280, 460, WindowRules.DefaultSize));
            RegisterAllHotkeys();
            settingsSaveTimer.Stop();
            SaveSettingsNow();
            showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
            SetDataStatus("程序设置已恢复默认");
            UpdateSettingsUi();
        }

        private async Task ClearLocalBrowsingDataAsync()
        {
            if (!webViewReady || webView.CoreWebView2 == null)
            {
                ShowOwnedMessage("网页组件尚未准备好，请稍后再试。", "波妞摸鱼", MessageBoxIcon.Information);
                return;
            }
            if (!ShowConfirmation("确定清除抖音登录和网页缓存吗？\r\n\r\n清除后需要重新登录，此操作无法撤销。",
                "清除本地登录数据")) return;

            SetDataStatus("正在清除，请稍候");
            try
            {
                await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
                webView.CoreWebView2.Navigate(DouyinUrl);
                SetDataStatus("登录和网页缓存已清除");
            }
            catch (Exception exception)
            {
                SetDataStatus("清除失败");
                ShowOwnedMessage("无法清除本地登录数据。\r\n\r\n" + exception.Message,
                    "波妞摸鱼", MessageBoxIcon.Error);
            }
        }

        private bool ShowConfirmation(string message, string caption)
        {
            suppressInactiveHide = true;
            try
            {
                return MessageBox.Show(this, message, caption, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            finally
            {
                suppressInactiveHide = false;
                showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
            }
        }

        private void ShowOwnedMessage(string message, string caption, MessageBoxIcon icon)
        {
            suppressInactiveHide = true;
            try { MessageBox.Show(this, message, caption, MessageBoxButtons.OK, icon); }
            finally
            {
                suppressInactiveHide = false;
                showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
            }
        }

        private void BeginShortcutCapture(string target)
        {
            if (captureTarget == target)
            {
                CancelShortcutCapture();
                return;
            }
            if (captureTarget != null) CancelShortcutCapture();
            captureTarget = target;
            UnregisterTargetHotkey(target);
            RoundedButton button = ShortcutButtonFor(target);
            Label status = ShortcutStatusFor(target);
            button.Text = "按下组合键";
            button.FaceColor = UiPalette.ShortcutCapturing;
            status.Text = "正在录入，Esc 取消";
            button.Focus();
            outsideSince = null;
            LayoutSettingsPanel();
        }

        private void CaptureShortcutKey(object sender, KeyEventArgs e)
        {
            if (captureTarget == null) return;
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Escape)
            {
                CancelShortcutCapture();
                return;
            }

            HotkeyDefinition candidate = HotkeyDefinition.FromKeyEvent(e);
            Label status = ShortcutStatusFor(captureTarget);
            if (candidate == null || candidate.IsReservedByWindows())
            {
                status.Text = "请按至少包含一个修饰键的组合键";
                return;
            }
            if (ConflictsWithAnyRegistered(captureTarget, candidate))
            {
                status.Text = "不能与其他快捷键相同";
                return;
            }

            if (!RegisterHotkey(HotkeyIdFor(captureTarget), candidate))
            {
                status.Text = "该快捷键已被其他程序占用";
                return;
            }

            if (captureTarget == "window") windowHotkey = candidate;
            else if (captureTarget == "chrome") chromeHotkey = candidate;
            else immersiveHotkey = candidate;
            SetHotkeyAvailable(captureTarget, true);
            PersistShortcut(captureTarget, candidate);
            captureTarget = null;
            ScheduleSettingsSave();
            UpdateSettingsUi();
        }

        private RoundedButton ShortcutButtonFor(string target)
        {
            if (target == "window") return windowShortcutButton;
            return target == "chrome" ? chromeShortcutButton : immersiveShortcutButton;
        }

        private Label ShortcutStatusFor(string target)
        {
            if (target == "window") return windowShortcutStatus;
            return target == "chrome" ? chromeShortcutStatus : immersiveShortcutStatus;
        }

        private int HotkeyIdFor(string target)
        {
            if (target == "window") return HotkeyWindow;
            return target == "chrome" ? HotkeyChrome : HotkeyImmersive;
        }

        private HotkeyDefinition HotkeyFor(string target)
        {
            if (target == "window") return windowHotkey;
            return target == "chrome" ? chromeHotkey : immersiveHotkey;
        }

        private void SetHotkeyAvailable(string target, bool available)
        {
            if (target == "window") windowHotkeyAvailable = available;
            else if (target == "chrome") chromeHotkeyAvailable = available;
            else immersiveHotkeyAvailable = available;
        }

        private void PersistShortcut(string target, HotkeyDefinition definition)
        {
            if (target == "window") settings.HideShortcut = definition.Serialize();
            else if (target == "chrome") settings.ChromeShortcut = definition.Serialize();
            else settings.ImmersiveShortcut = definition.Serialize();
        }

        private bool ConflictsWithAnyRegistered(string target, HotkeyDefinition candidate)
        {
            string[] others = { "window", "chrome", "immersive" };
            foreach (string other in others)
            {
                if (other == target) continue;
                if (candidate.ConflictsWith(HotkeyFor(other))) return true;
            }
            return candidate.ConflictsWith(muteHotkey);
        }

        private void CancelShortcutCapture()
        {
            if (captureTarget == null) return;
            string target = captureTarget;
            captureTarget = null;
            SetHotkeyAvailable(target, RegisterHotkey(HotkeyIdFor(target), HotkeyFor(target)));
            UpdateSettingsUi();
        }

        private void RegisterAllHotkeys()
        {
            windowHotkeyAvailable = RegisterHotkey(HotkeyWindow, windowHotkey);
            chromeHotkeyAvailable = RegisterHotkey(HotkeyChrome, chromeHotkey);
            muteHotkeyAvailable = RegisterHotkey(HotkeyMute, muteHotkey);
            immersiveHotkeyAvailable = RegisterHotkey(HotkeyImmersive, immersiveHotkey);
            if (!windowHotkeyAvailable)
            {
                MessageBox.Show(this,
                    windowHotkey.Display() + " 已被占用。为避免隐藏后无法恢复，本次运行已停用鼠标移出自动隐藏。",
                    "波妞摸鱼快捷键不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (!chromeHotkeyAvailable)
            {
                ApplyChromeState(false);
                MessageBox.Show(this,
                    chromeHotkey.Display() + " 已被占用，无边框模式已保持关闭。请在设置中更换快捷键。",
                    "波妞摸鱼边框快捷键不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (!immersiveHotkeyAvailable)
            {
                MessageBox.Show(this,
                    immersiveHotkey.Display() + " 已被占用，清爽模式快捷键本次不可用。请在设置中更换快捷键。",
                    "波妞摸鱼清爽模式快捷键不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateSettingsUi();
        }

        private bool RegisterHotkey(int id, HotkeyDefinition definition)
        {
            NativeMethods.UnregisterHotKey(Handle, id);
            return NativeMethods.RegisterHotKey(Handle, id, definition.NativeModifiers, (uint)definition.Key);
        }

        private void UnregisterTargetHotkey(string target)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyIdFor(target));
            SetHotkeyAvailable(target, false);
        }

        private void UnregisterAllHotkeys()
        {
            if (!IsHandleCreated) return;
            NativeMethods.UnregisterHotKey(Handle, HotkeyWindow);
            NativeMethods.UnregisterHotKey(Handle, HotkeyChrome);
            NativeMethods.UnregisterHotKey(Handle, HotkeyMute);
            NativeMethods.UnregisterHotKey(Handle, HotkeyImmersive);
        }

        private void UpdateSettingsUi()
        {
            settings.AutoHideDelayMilliseconds = WindowRules.NormalizeAutoHideDelay(settings.AutoHideDelayMilliseconds);
            windowShortcutButton.Text = windowHotkey.Display();
            chromeShortcutButton.Text = chromeHotkey.Display();
            windowShortcutButton.FaceColor = UiPalette.ShortcutFace;
            chromeShortcutButton.FaceColor = UiPalette.ShortcutFace;
            windowShortcutStatus.Text = windowHotkeyAvailable || !IsHandleCreated ? "当前组合键" : "快捷键不可用，请重新设置";
            chromeShortcutStatus.Text = chromeHotkeyAvailable || !IsHandleCreated ? "当前组合键" : "快捷键不可用，请重新设置";
            autoHideToggle.Checked = settings.AutoHideEnabled;
            autoHideToggle.Enabled = windowHotkeyAvailable || !IsHandleCreated;
            bool delayRowVisible = (settings.AutoHideEnabled && windowHotkeyAvailable) || !IsHandleCreated;
            if (delayRow != null) delayRow.Hidden = !delayRowVisible;
            autoHideDelayPicker.Enabled = settings.AutoHideEnabled;
            autoHideDelayPicker.SelectedIndex = settings.AutoHideDelayMilliseconds == 500 ? 2
                : settings.AutoHideDelayMilliseconds == 300 ? 1 : 0;
            autoHideStatus.Text = !windowHotkeyAvailable && IsHandleCreated
                ? "显示快捷键不可用，本次已停用"
                : settings.AutoHideEnabled ? "鼠标离开窗口后自动收起" : "已关闭";
            inactiveHideToggle.Checked = settings.HideWhenInactive;
            inactiveHideStatus.Text = settings.HideWhenInactive ? "切换到其他程序时隐藏" : "已关闭；锁屏时仍会隐藏";
            landscapeToggle.Checked = settings.AutoLandscapeLive;
            liveStatus.Text = currentIsLive && settings.AutoLandscapeLive ? "直播中" : settings.AutoLandscapeLive ? "已开启" : "已关闭";
            leftNavToggle.Checked = settings.HideLeftNav;
            leftNavStatus.Text = settings.HideLeftNav ? "已隐藏" : "已显示";
            topBarToggle.Checked = settings.HideTopBar;
            topBarStatus.Text = settings.HideTopBar ? "已隐藏" : "已显示";
            rightBarToggle.Checked = settings.HideRightBar;
            rightBarStatus.Text = settings.HideRightBar ? "已隐藏" : "已显示";
            immersiveShortcutButton.Text = immersiveHotkey.Display();
            immersiveShortcutButton.FaceColor = UiPalette.ShortcutFace;
            immersiveShortcutStatus.Text = immersiveHotkeyAvailable || !IsHandleCreated
                ? "点击组合键可重新设置" : "快捷键不可用，请重新设置";
            toolTip.SetToolTip(immersiveShortcutButton,
                "进入/退出清爽模式 (" + immersiveHotkey.Display() + ")；点击可修改");
            immersiveToggle.Checked = settings.ImmersiveMode;
            immersiveStatus.Text = settings.ImmersiveMode
                ? "只保留视频和播放进度条" : "已关闭";
            LayoutSettingsPanel();
            UpdateToolbarState();
        }

        private void UpdateToolbarState()
        {
            pinButton.Active = TopMost;
            muteButton.Active = settings.Muted;
            settingsButton.Active = settingsOpen;
            hideButton.Enabled = windowHotkeyAvailable || !IsHandleCreated;
            chromeButton.Enabled = chromeHotkeyAvailable || !IsHandleCreated;
            toolTip.SetToolTip(pinButton, TopMost ? "取消置顶" : "始终置顶");
            toolTip.SetToolTip(muteButton, settings.Muted ? "恢复声音" : "静音");
            toolTip.SetToolTip(hideButton, "立即隐藏 (" + windowHotkey.Display() + ")");
            toolTip.SetToolTip(chromeButton, "隐藏边框 (" + chromeHotkey.Display() + ")");
        }

        private async void ApplyPageElementVisibility()
        {
            Diagnostics.Mark("ApplyPageElementVisibility enter");
            if (!webViewReady || webView.CoreWebView2 == null) return;
            if (settingsOpen || !webView.Visible)
            {
                Diagnostics.Mark("ApplyPageElementVisibility deferred while WebView hidden");
                return;
            }
            try { await webView.ExecuteScriptAsync(BuildPageVisibilityScript()); Diagnostics.Mark("ApplyPageElementVisibility done"); }
            catch (Exception exception) { Diagnostics.LogException("ApplyPageElementVisibility", exception); }
        }

        private string BuildPageVisibilityScript()
        {
            bool hideLeft = settings.HideLeftNav;
            bool hideTop = settings.HideTopBar;
            bool hideRight = settings.HideRightBar;
            bool immersive = settings.ImmersiveMode;
            return @"(function(){
  var S='__boniuPageStyle';
  var st=document.getElementById(S);
  if(!st){st=document.createElement('style');st.id=S;document.head.appendChild(st);}
  var css='';
  // 始终隐藏左右切换箭头（无需开关）
  css+='div[aria-label=""上一条""],div[aria-label=""下一条""],';
  css+='[class*=""switch-btn""],[class*=""switchBtn""],';
  css+='[class*=""arrow-left""],[class*=""arrow-right""],';
  css+='[class*=""slideArrow""],[class*=""slide-arrow""]';
  css+='{display:none!important;}';
  // 隐藏左侧导航栏
  if(" + (hideLeft ? "true" : "false") + @"){
    css+='div[data-e2e=""douyin-navigation""],';
    css+='[class*=""SideBar""],[class*=""side-bar""],';
    css+='[class*=""Navigation""],';
    css+='div[class*=""leftSidebar""],div[class*=""left-sidebar""],';
    css+='[data-e2e=""left-sidebar""]';
    css+='{display:none!important;}';
    // 收起左侧后让内容区占满
    css+='[class*=""MainContent""],[class*=""main-content""],';
    css+='div[class*=""ContentLayout""]';
    css+='{margin-left:0!important;padding-left:0!important;width:100%!important;max-width:100%!important;}';
  }
  // 隐藏顶部搜索栏区域
  if(" + (hideTop ? "true" : "false") + @"){
    css+='div[data-e2e=""searchbar""],';
    css+='[class*=""Header""]:not([class*=""HeaderLayout""]),';
    css+='header,[class*=""header""]:not([class*=""player""]):not([class*=""Player""]),';
    css+='[class*=""TopBar""],[class*=""top-bar""],';
    css+='[class*=""SearchBar""],[class*=""search-bar""],';
    css+='[data-e2e=""top-banner""],div[class*=""topBar""]';
    css+='{display:none!important;}';
    // 收起顶部后让内容区上移
    css+='[class*=""MainContent""],[class*=""main-content""],';
    css+='div[class*=""ContentLayout""],';
    css+='div[class*=""FeedContainer""],[class*=""feed-container""]';
    css+='{margin-top:0!important;padding-top:0!important;height:100%!important;}';
  }
  // 隐藏右侧互动区
  if(" + (hideRight ? "true" : "false") + @"){
    css+='[data-e2e=""video-sidebar""],';
    css+='[class*=""SideToolbar""],[class*=""side-toolbar""],';
    css+='[class*=""ActionBar""],[class*=""action-bar""],';
    css+='[class*=""InteractBar""],[class*=""interact-bar""],';
    css+='[class*=""right-bar""],[class*=""RightBar""],';
    css+='[class*=""video-sidebar""],';
    css+='div[class*=""SideToolBar""]';
    css+='{display:none!important;}';
  }
  // 清爽模式只隐藏页面装饰，不接管抖音的播放器、视频素材或虚拟列表布局。
  // 播放器外框比例、内部视频适配和上下切换全部沿用抖音原生实现。
  // 抖音的 CSS-in-JS 在运行时注入到 head 末尾，同特异性的 !important 会靠源码顺序压过我们，
  // 所以这里每条规则都用根节点 #dark 把特异性抬到 id 级。
  if(" + (immersive ? "true" : "false") + @"){
    css+='#dark #douyin-header,#dark header,';
    css+='#dark #douyin-navigation,#dark [data-e2e=""douyin-navigation""],';
    css+='#dark [data-e2e=""searchbar-input""],#dark [data-e2e=""searchbar-button""],';
    css+='#dark [data-e2e=""im-entry""],#dark [data-e2e=""something-button""],';
    css+='#dark [class*=""danmaku""],';
    css+='#dark [data-e2e=""video-info""]';
    css+='{display:none!important;}';
    // 移除顶部栏占位和互动栏额外留白，但保留互动按钮本身；按钮叠放在铺满宽度的视频区内。
    // 不修改播放器、slide 或 video，比例和虚拟列表切换仍由抖音负责。
    css+='#dark #douyin-right-container{padding-top:0!important;}';
    css+='#dark [data-e2e=""slideList""]{padding-right:0!important;height:100%!important;}';
  }
  st.textContent=css;
  st.setAttribute('data-fallback','0');
  // 清理旧版本留下的强制 resize 对齐守卫；原生布局不需要持续干预。
  st.setAttribute('data-immersive'," + (immersive ? "'1'" : "'0'") + @");
  var G='__boniuAlignGuard';
  if(window[G]){clearInterval(window[G]);window[G]=0;}
  // 无侵入健康检查：只观察当前活动视频是否仍覆盖页面中心；不 resize、不改播放器定位。
  // 连续两次异常时清空注入样式，回退抖音原始布局，避免页面改版后黑屏。
  var H='__boniuImmersiveHealthGuard';
  if(window[H]){clearInterval(window[H]);window[H]=0;}
  if(" + (immersive ? "true" : "false") + @"){
    var misses=0;
    var coversCenter=function(el){
      if(!el)return false;
      var r=el.getBoundingClientRect(),cs=getComputedStyle(el);
      return cs.display!=='none'&&cs.visibility!=='hidden'&&r.width>innerWidth*.4&&r.height>innerHeight*.4&&
        r.left<=innerWidth/2&&r.right>=innerWidth/2&&r.top<=innerHeight/2&&r.bottom>=innerHeight/2;
    };
    window[H]=setInterval(function(){
      var s=document.getElementById(S);
      if(!s||s.getAttribute('data-immersive')!=='1'){clearInterval(window[H]);window[H]=0;return;}
      var active=document.querySelector('[data-e2e=""feed-active-video""]');
      var videos=active?active.querySelectorAll('video'):document.querySelectorAll('video');
      if(!videos.length)return;
      var ok=coversCenter(active);
      if(ok){var any=false;videos.forEach(function(v){if(coversCenter(v))any=true;});ok=any;}
      misses=ok?0:misses+1;
      if(misses<2)return;
      s.textContent='';s.setAttribute('data-fallback','1');
      clearInterval(window[H]);window[H]=0;
      try{window.chrome.webview.postMessage('boniu:immersive-fallback');}catch(e){}
    },1500);
  }
})()";
        }

        private void UpdateZoom()
        {
            if (webViewReady) webView.ZoomFactor = WindowRules.CalculateZoomFactor(ClientSize.Width);
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
            settings.ImmersiveMode = !settings.ImmersiveMode;
            toolbarRevealed = false;
            Diagnostics.Log("immersive=" + settings.ImmersiveMode + " source=hotkey");
            ScheduleSettingsSave();
            LayoutWindow();
            UpdateSettingsUi();
            ApplyPageElementVisibility();
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

            int count = 0;
            for (int i = 0; i < toolbar.Controls.Count; i++)
            {
                RoundedButton existing = toolbar.Controls[i] as RoundedButton;
                if (existing == null) continue;
                count++;
                existing.CornerRadius = radius;
            }
            if (count > 0 && count * buttonWidth + (count - 1) * gap > available)
            {
                buttonWidth = Math.Max(Scaled(14), (available - (count - 1) * gap) / count);
                gap = Scaled(1);
                if (count > 1 && count * buttonWidth + (count - 1) * gap > available)
                    gap = Math.Max(0, (available - count * buttonWidth) / (count - 1));
            }

            int top = (toolbarHeight - buttonHeight) / 2;
            int x = right;
            for (int i = toolbar.Controls.Count - 1; i >= 0; i--)
            {
                RoundedButton button = toolbar.Controls[i] as RoundedButton;
                if (button == null) continue;
                x -= buttonWidth;
                button.SetBounds(x, top, buttonWidth, buttonHeight);
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

        private void ScheduleSettingsSave()
        {
            if (smokeTest || liveSmokeTest || settingsSmokeTest || domProbe) return;
            settingsSaveTimer.Stop();
            settingsSaveTimer.Start();
        }

        private void SaveSettingsNow()
        {
            if (smokeTest || liveSmokeTest || settingsSmokeTest || domProbe) return;
            settings.AlwaysOnTop = TopMost;
            settings.HideShortcut = windowHotkey.Serialize();
            settings.ChromeShortcut = chromeHotkey.Serialize();
            settings.ImmersiveShortcut = immersiveHotkey.Serialize();
            try { settingsStore.Save(settings); }
            catch { statusDot.State = StatusDotState.Error; }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (isQuitting) return;
            e.Cancel = true;
            HideWindow();
        }

        private void QuitApplication()
        {
            isQuitting = true;
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
            button.Font = new Font("Microsoft YaHei UI", 8F);
            button.Cursor = Cursors.Hand;
            button.AccessibleRole = AccessibleRole.PushButton;
            return button;
        }

        private static Label CreateStatusLabel()
        {
            Label label = new Label();
            label.Text = "当前组合键";
            label.Font = new Font("Microsoft YaHei UI", 7F);
            label.ForeColor = UiPalette.TextSecondary;
            label.BackColor = UiPalette.Card;
            label.AutoSize = true;
            return label;
        }

        private const string PauseVideosScript = @"(() => {
            globalThis.__miniViewResumeVideos = Array.from(document.querySelectorAll('video'))
              .filter(video => !video.paused && !video.ended);
            globalThis.__miniViewResumeVideos.forEach(video => video.pause());
          })()";

        private const string ResumeVideosScript = @"(() => {
            const videos = globalThis.__miniViewResumeVideos || [];
            globalThis.__miniViewResumeVideos = [];
            videos.forEach(video => video.isConnected && video.play().catch(() => {}));
          })()";

        private const string OpenRecommendScript = @"(() => {
            const nav = document.querySelector('[data-e2e=""douyin-navigation""]');
            const root = nav || document;
            const link = root.querySelector('a[href*=""recommend""]');
            if (link) { link.click(); return 'link ' + link.getAttribute('href'); }
            const nodes = root.querySelectorAll('div,span,li,a,p');
            for (let i = 0; i < nodes.length; i++) {
              const el = nodes[i];
              if ((el.textContent || '').trim() !== '推荐') continue;
              const box = el.getBoundingClientRect();
              if (box.width <= 0 || box.height <= 0) continue;
              el.click();
              return 'text ' + el.tagName;
            }
            return 'none';
          })()";

        private const string DomProbeScript = @"(() => {
            const pick = (el) => {
              if (!el) return null;
              const r = el.getBoundingClientRect();
              const cs = getComputedStyle(el);
              let cls = '';
              try { cls = String(el.className).slice(0, 90); } catch (e) { cls = ''; }
              return { tag: el.tagName, id: el.id || null, cls: cls,
                e2e: el.getAttribute ? el.getAttribute('data-e2e') : null,
                x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height),
                pos: cs.position, disp: cs.display, of: cs.objectFit,
                mr: cs.marginRight, pr: cs.paddingRight, mb: cs.marginBottom, pb: cs.paddingBottom, z: cs.zIndex,
                tf: cs.transform === 'none' ? '' : cs.transform.slice(0, 40),
                ml: cs.marginLeft, pl: cs.paddingLeft, cl: cs.left, ct: cs.top, ov: cs.overflow };
            };
            const out = {};
            out.viewport = { w: innerWidth, h: innerHeight, dpr: devicePixelRatio, url: location.href };
            const videos = Array.prototype.slice.call(document.querySelectorAll('video'));
            let video = null, best = 0;
            videos.forEach(v => {
              const r = v.getBoundingClientRect();
              if (r.width * r.height > best) { best = r.width * r.height; video = v; }
            });
            out.videoCount = videos.length;
            out.video = pick(video);
            out.intrinsic = video && video.videoWidth
              ? { w: video.videoWidth, h: video.videoHeight, ratio: video.videoWidth / video.videoHeight } : null;
            out.ancestors = [];
            let cur = video;
            for (let i = 0; i < 18 && cur; i++) { cur = cur.parentElement; if (cur) out.ancestors.push(pick(cur)); }
            out.byId = {};
            const darkRoot = document.getElementById('dark');
            out.darkChildren = darkRoot ? Array.prototype.map.call(darkRoot.children, pick) : [];
            const rightRoot = document.getElementById('douyin-right-container');
            out.rightTree = [];
            if (rightRoot) rightRoot.querySelectorAll('*').forEach(el => {
              const p = pick(el);
              if (!p || p.w <= 0 || p.h <= 0 || out.rightTree.length >= 120) return;
              if (p.y <= 60 || p.pr !== '0px' || p.mb !== '0px' || p.pb !== '0px') {
                const parent = el.parentElement;
                p.parent = parent ? ((parent.id ? '#' + parent.id : '') + '.' + String(parent.className || '').slice(0, 60)) : '';
                out.rightTree.push(p);
              }
            });
            ['HeaderLayout', 'ContainerBackgroundLayout', 'LeftBackgroundLayout', 'RightBackgroundLayout',
             'RightPanelLayout', 'PlayerLayout', 'BottomLayout', 'GiftMenuLayout', 'FeedItemLayout']
              .forEach(id => { out.byId[id] = pick(document.getElementById(id)); });
            out.e2e = [];
            document.querySelectorAll('[data-e2e]').forEach(el => {
              const p = pick(el);
              if (p && p.w > 0 && p.h > 0 && out.e2e.length < 45) out.e2e.push(p);
            });
            const at = (x, y) => document.elementsFromPoint(x, y).slice(0, 7).map(pick);
            out.rightEdge = at(innerWidth - 6, innerHeight * 0.5);
            out.midRight = at(innerWidth * 0.88, innerHeight * 0.5);
            out.bottomEdge = at(innerWidth * 0.5, innerHeight - 6);
            out.topEdge = at(innerWidth * 0.5, 6);
            out.center = at(innerWidth * 0.5, innerHeight * 0.45);
            return out;
          })()";

        private const string LiveProbeScript = @"(() => {
            const live = Boolean(document.querySelector('#PlayerLayout > .__livingPlayer__, [data-e2e=""living-container""] #PlayerLayout'));
            document.documentElement.style.setProperty('overflow-x', 'hidden', 'important');
            document.body && document.body.style.setProperty('overflow-x', 'hidden', 'important');
            document.querySelectorAll('video').forEach(video => video.style.setProperty('max-width', '100vw', 'important'));
            if (!live) return false;
            ['HeaderLayout', 'GiftMenuLayout', 'BottomLayout', 'RightBackgroundLayout', 'RightPanelLayout'].forEach(id => {
              const element = document.getElementById(id);
              if (element) element.style.setProperty('display', 'none', 'important');
            });
            ['ContainerBackgroundLayout', 'LeftBackgroundLayout', 'PlayerLayout'].forEach(id => {
              const element = document.getElementById(id);
              if (!element) return;
              element.style.setProperty('width', '100%', 'important');
              element.style.setProperty('height', '100%', 'important');
              element.style.setProperty('max-width', 'none', 'important');
              element.style.setProperty('margin', '0', 'important');
            });
            document.querySelectorAll('#PlayerLayout > .__livingPlayer__, #PlayerLayout > .__livingPlayer__ > [data-anchor-id=""living-basic-player""]').forEach(element => {
              element.style.setProperty('width', '100%', 'important');
              element.style.setProperty('height', '100%', 'important');
              element.style.setProperty('max-width', 'none', 'important');
              element.style.setProperty('padding-top', '0', 'important');
            });
            return true;
          })()";

        private const string LiveSmokeTestHtml = @"<!doctype html><html><body style=""margin:0;background:#101010;color:white"">
          <div id=""HeaderLayout"" style=""height:50px;background:red"">header</div>
          <div id=""ContainerBackgroundLayout"" style=""display:flex;width:50%;height:200px;background:#222"">
            <div id=""LeftBackgroundLayout""><div id=""PlayerLayout""><div class=""__livingPlayer__"" style=""background:#15803d"">video</div></div></div>
            <div id=""RightPanelLayout"" style=""width:180px;background:blue"">chat</div>
          </div>
          <div id=""GiftMenuLayout"" style=""height:80px;background:red"">gift</div>
          <div id=""BottomLayout"" style=""height:88px;background:red"">gift shortcuts</div>
        </body></html>";

        private enum SettingsRowKind { Inline, Stacked, FullWidth }

        private sealed class SettingsRow
        {
            internal SettingsRowKind Kind;
            internal Label TitleLabel;
            internal Label Status;
            internal Control Editor;
            internal int EditorWidth;
            internal int EditorHeight;
            internal bool Hidden;
        }

        private sealed class SettingsCard
        {
            internal Label TitleLabel;
            internal readonly List<SettingsRow> Rows = new List<SettingsRow>();
            internal readonly List<Rectangle> RowRects = new List<Rectangle>();
            internal Rectangle Bounds;
        }
    }

    internal static class UiPalette
    {
        internal static readonly Color Page = Color.FromArgb(0, 0, 0);
        internal static readonly Color Card = Color.FromArgb(28, 28, 30);
        internal static readonly Color Separator = Color.FromArgb(56, 56, 60);
        internal static readonly Color TextPrimary = Color.FromArgb(255, 255, 255);
        internal static readonly Color TextSecondary = Color.FromArgb(152, 152, 157);
        internal static readonly Color TextTertiary = Color.FromArgb(99, 99, 102);
        internal static readonly Color Accent = Color.FromArgb(48, 209, 88);
        internal static readonly Color AccentHover = Color.FromArgb(78, 219, 112);
        internal static readonly Color AccentPressed = Color.FromArgb(38, 178, 72);
        internal static readonly Color Ink = Color.FromArgb(72, 72, 74);
        internal static readonly Color InkHover = Color.FromArgb(88, 88, 92);
        internal static readonly Color InkPressed = Color.FromArgb(108, 108, 112);
        internal static readonly Color Destructive = Color.FromArgb(255, 69, 58);
        internal static readonly Color Shell = Color.FromArgb(23, 23, 23);
        internal static readonly Color Toolbar = Color.FromArgb(32, 32, 32);
        internal static readonly Color ToolbarGlyph = Color.FromArgb(228, 228, 231);
        internal static readonly Color ToolbarHover = Color.FromArgb(63, 63, 70);
        internal static readonly Color ToolbarPressed = Color.FromArgb(80, 80, 88);
        internal static readonly Color ToolbarActive = Color.FromArgb(23, 64, 42);
        internal static readonly Color CloseHover = Color.FromArgb(140, 43, 28);
        internal static readonly Color ClosePressed = Color.FromArgb(168, 52, 34);
        internal static readonly Color ButtonFace = Color.FromArgb(44, 44, 46);
        internal static readonly Color ShortcutFace = Color.FromArgb(38, 38, 42);
        internal static readonly Color ShortcutCapturing = Color.FromArgb(23, 61, 38);
    }

    internal static class UiPaint
    {
        internal static GraphicsPath RoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0F || rect.Height <= 0F) return path;
            float diameter = Math.Min(radius * 2F, Math.Min(rect.Width, rect.Height));
            if (diameter <= 0F)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180F, 90F);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270F, 90F);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0F, 90F);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90F, 90F);
            path.CloseFigure();
            return path;
        }

        internal static GraphicsPath CapsulePath(RectangleF rect)
        {
            return RoundedPath(rect, rect.Height / 2F);
        }

        internal static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(from.A,
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;
        private bool active;
        private Color faceColor = UiPalette.ButtonFace;

        internal RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            BackColor = UiPalette.Card;
            CornerRadius = 8;
            TabStop = false;
            UseMnemonic = false;
        }

        /// <summary>Idle fill. Color.Transparent keeps the button flat until hover or activation.</summary>
        internal Color FaceColor
        {
            get { return faceColor; }
            set { faceColor = value; Invalidate(); }
        }

        internal Color HoverFaceColor { get; set; }

        internal Color PressedFaceColor { get; set; }

        internal Color ActiveFaceColor { get; set; }

        internal Color ActiveForeColor { get; set; }

        internal bool Active
        {
            get { return active; }
            set { active = value; Invalidate(); }
        }

        internal int CornerRadius { get; set; }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color face = ResolveFace();
            Color text = ResolveFore();

            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (face.A > 0)
            {
                RectangleF rect = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
                using (GraphicsPath path = UiPaint.RoundedPath(rect, CornerRadius))
                using (SolidBrush brush = new SolidBrush(face))
                    e.Graphics.FillPath(brush, path);
            }
            if (Text.Length == 0) return;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private Color ResolveFace()
        {
            if (!Enabled) return UiPaint.Blend(faceColor, BackColor, 0.55F);
            if (active && !ActiveFaceColor.IsEmpty) return ActiveFaceColor;
            if (pressed) return PressedFaceColor.IsEmpty
                ? UiPaint.Blend(faceColor, Color.White, 0.22F) : PressedFaceColor;
            if (hovered) return HoverFaceColor.IsEmpty
                ? UiPaint.Blend(faceColor, Color.White, 0.12F) : HoverFaceColor;
            return faceColor;
        }

        private Color ResolveFore()
        {
            Color fore = active && !ActiveForeColor.IsEmpty ? ActiveForeColor : ForeColor;
            return Enabled ? fore : UiPaint.Blend(fore, BackColor, 0.5F);
        }
    }

    internal sealed class SegmentedPicker : Control
    {
        private static readonly Color ContainerColor = Color.FromArgb(58, 58, 60);
        private static readonly Color SelectedColor = Color.FromArgb(120, 120, 128);
        private static readonly Color HoveredColor = Color.FromArgb(78, 78, 82);
        private static readonly Color SelectedTextColor = Color.FromArgb(255, 255, 255);
        private static readonly Color TextColor = Color.FromArgb(174, 174, 178);
        private static readonly Color DisabledColor = Color.FromArgb(99, 99, 102);

        private readonly List<string> items = new List<string>();
        private int selectedIndex = -1;
        private bool hovered;
        private int hoveredIndex = -1;

        internal SegmentedPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            BackColor = UiPalette.Card;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.List;
        }

        internal List<string> Items { get { return items; } }

        internal event EventHandler SelectedIndexChanged;

        internal int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int next = value < 0 && value != -1 ? 0 : (value >= items.Count ? selectedIndex : value);
                if (next == selectedIndex) return;
                selectedIndex = next;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        private float SegmentWidth
        {
            get { return items.Count == 0 ? Width : (Width - 4F) / items.Count; }
        }

        private void SelectAt(int x)
        {
            if (items.Count == 0) return;
            int index = (int)Math.Floor((x - 2F) / SegmentWidth);
            if (index < 0) index = 0;
            if (index > items.Count - 1) index = items.Count - 1;
            SelectedIndex = index;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus();
            SelectAt(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            hovered = true;
            int index = items.Count == 0 ? -1 : (int)Math.Floor((e.X - 2F) / SegmentWidth);
            if (index < 0 || index > items.Count - 1) index = -1;
            if (index == hoveredIndex) return;
            hoveredIndex = index;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            hoveredIndex = -1;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || items.Count == 0) return;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) SelectedIndex = Math.Max(0, selectedIndex - 1);
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) SelectedIndex = Math.Min(items.Count - 1, selectedIndex + 1);
            else if (e.KeyCode == Keys.Home) SelectedIndex = 0;
            else if (e.KeyCode == Keys.End) SelectedIndex = items.Count - 1;
            else return;
            e.Handled = true;
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF outer = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = UiPaint.CapsulePath(outer))
            using (SolidBrush brush = new SolidBrush(ContainerColor))
                graphics.FillPath(brush, path);
            if (items.Count == 0) return;

            float width = SegmentWidth;
            for (int i = 0; i < items.Count; i++)
            {
                RectangleF cell = new RectangleF(2F + i * width, 2F, width, Height - 4F);
                bool selected = i == selectedIndex;
                if (selected || (Enabled && hovered && i == hoveredIndex))
                {
                    Color pill = selected
                        ? (Enabled ? SelectedColor : DisabledColor)
                        : HoveredColor;
                    using (GraphicsPath path = UiPaint.CapsulePath(
                        new RectangleF(cell.X + 0.5F, cell.Y + 0.5F, cell.Width - 1F, cell.Height - 1F)))
                    using (SolidBrush brush = new SolidBrush(pill))
                        graphics.FillPath(brush, path);
                }

                Color text = !Enabled ? DisabledColor : selected ? SelectedTextColor : TextColor;
                TextRenderer.DrawText(graphics, items[i], Font, Rectangle.Round(cell), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal enum StatusDotState { Ready, Loading, Error }

    internal sealed class StatusDot : Control
    {
        private StatusDotState state;

        internal StatusDot()
        {
            Size = new Size(7, 7);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        internal StatusDotState State
        {
            get { return state; }
            set { state = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color color = state == StatusDotState.Loading ? Color.FromArgb(245, 158, 11)
                : state == StatusDotState.Error ? Color.FromArgb(239, 68, 68)
                : Color.FromArgb(34, 197, 94);
            e.Graphics.Clear(BackColor);
            using (SolidBrush brush = new SolidBrush(color)) e.Graphics.FillEllipse(brush, ClientRectangle);
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        private bool hovered;
        private bool pressed;

        internal ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            BackColor = UiPalette.Card;
            Size = new Size(44, 26);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color track;
            if (!Enabled) track = Checked ? Color.FromArgb(24, 92, 46) : Color.FromArgb(58, 58, 60);
            else if (Checked) track = pressed ? UiPalette.AccentPressed : (hovered ? UiPalette.AccentHover : UiPalette.Accent);
            else track = pressed ? UiPalette.InkPressed : (hovered ? UiPalette.InkHover : UiPalette.Ink);

            RectangleF trackRect = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = UiPaint.CapsulePath(trackRect))
            using (SolidBrush brush = new SolidBrush(track))
                graphics.FillPath(brush, path);

            float inset = Math.Max(2F, Height * 0.077F);
            float knob = Height - inset * 2F;
            float knobX = Checked ? Width - knob - inset : inset;
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(52, 0, 0, 0)))
                graphics.FillEllipse(shadow, knobX + 0.7F, inset + 1.3F, knob, knob);
            using (SolidBrush face = new SolidBrush(Enabled ? Color.White : Color.FromArgb(216, 216, 216)))
                graphics.FillEllipse(face, knobX, inset, knob, knob);
        }
    }

    internal sealed class DarkScrollPanel : Panel
    {
        private const int ScrollBarWidth = 8;
        private const int ThumbMinHeight = 28;
        private const int SB_VERT = 1;

        private bool dragging;
        private int dragStartMouseY;
        private int dragStartScrollY;
        private bool hoverThumb;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        internal DarkScrollPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(23, 23, 23);
        }

        internal int ScrollContentHeight
        {
            get { return AutoScrollMinSize.Height; }
            set
            {
                // Re-assigning AutoScrollMinSize forces a layout pass that can toggle the native
                // scrollbar and change ClientSize, which re-triggers Resize. Skip no-op writes.
                if (AutoScrollMinSize.Height == value) return;
                AutoScrollMinSize = new Size(0, value);
                HideNativeScrollBar();
                Invalidate();
            }
        }

        internal int ScrollOffsetY
        {
            get { return -AutoScrollPosition.Y; }
        }

        internal bool CanScroll
        {
            get { return AutoScrollMinSize.Height > ClientSize.Height; }
        }

        internal void SetScrollOffset(int offsetY)
        {
            AutoScrollPosition = new Point(0, Math.Max(0, offsetY));
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HideNativeScrollBar();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            HideNativeScrollBar();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            HideNativeScrollBar();
        }

        private void HideNativeScrollBar()
        {
            if (IsHandleCreated) ShowScrollBar(Handle, SB_VERT, false);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0085 || m.Msg == 0x0014) HideNativeScrollBar();
            if (m.Msg == 0x0115 || m.Msg == 0x00A5)
            {
                HideNativeScrollBar();
                Invalidate();
            }
        }

        private int MaxScroll
        {
            get { return Math.Max(0, ScrollContentHeight - ClientSize.Height); }
        }

        private int ThumbHeight
        {
            get
            {
                if (!CanScroll) return 0;
                return Math.Max(ThumbMinHeight, (int)((double)ClientSize.Height / ScrollContentHeight * ClientSize.Height));
            }
        }

        private int ThumbY
        {
            get
            {
                if (!CanScroll || MaxScroll <= 0) return 0;
                int trackHeight = ClientSize.Height - ThumbHeight;
                return (int)((double)ScrollOffsetY / MaxScroll * trackHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!CanScroll) return;

            Rectangle track = new Rectangle(Width - ScrollBarWidth, 0, ScrollBarWidth, Height);
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(18, 18, 20)))
                e.Graphics.FillRectangle(trackBrush, track);

            Rectangle thumb = new Rectangle(Width - ScrollBarWidth + 1, ThumbY, ScrollBarWidth - 2, ThumbHeight);
            Color thumbColor = dragging ? Color.FromArgb(100, 100, 108)
                : hoverThumb ? Color.FromArgb(82, 82, 91)
                : Color.FromArgb(63, 63, 70);
            using (SolidBrush thumbBrush = new SolidBrush(thumbColor))
                e.Graphics.FillRectangle(thumbBrush, thumb);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!CanScroll) { base.OnMouseWheel(e); return; }
            int newY = Math.Max(0, Math.Min(MaxScroll, ScrollOffsetY - e.Delta / 3));
            AutoScrollPosition = new Point(0, newY);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!CanScroll || e.Button != MouseButtons.Left || e.X < Width - ScrollBarWidth) return;

            if (e.Y >= ThumbY && e.Y <= ThumbY + ThumbHeight)
            {
                dragging = true;
                dragStartMouseY = e.Y;
                dragStartScrollY = ScrollOffsetY;
                Capture = true;
            }
            else
            {
                int direction = e.Y < ThumbY ? -1 : 1;
                int newY = Math.Max(0, Math.Min(MaxScroll, ScrollOffsetY + direction * ClientSize.Height));
                AutoScrollPosition = new Point(0, newY);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                int trackHeight = ClientSize.Height - ThumbHeight;
                if (trackHeight > 0)
                {
                    double ratio = (double)MaxScroll / trackHeight;
                    int newY = Math.Max(0, Math.Min(MaxScroll, dragStartScrollY + (int)((e.Y - dragStartMouseY) * ratio)));
                    AutoScrollPosition = new Point(0, newY);
                }
                Invalidate();
                return;
            }
            bool overThumb = CanScroll && e.X >= Width - ScrollBarWidth && e.Y >= ThumbY && e.Y <= ThumbY + ThumbHeight;
            if (overThumb != hoverThumb)
            {
                hoverThumb = overThumb;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!dragging && hoverThumb)
            {
                hoverThumb = false;
                Invalidate();
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.MouseWheel += ForwardMouseWheel;
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            e.Control.MouseWheel -= ForwardMouseWheel;
        }

        private void ForwardMouseWheel(object sender, MouseEventArgs e)
        {
            OnMouseWheel(e);
        }
    }
}
