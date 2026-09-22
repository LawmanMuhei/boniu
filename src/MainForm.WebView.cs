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
        private async Task InitializeWebViewAsync()
        {
            try
            {
                statusDot.State = StatusDotState.Loading;
                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(
                    "--autoplay-policy=no-user-gesture-required --disable-features=HardwareMediaKeyHandling");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                if (isQuitting || IsDisposed) return;
                await webView.EnsureCoreWebView2Async(environment);
                if (isQuitting || IsDisposed) return;

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
                core.NavigationCompleted += async delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (isQuitting || IsDisposed) return;
                    statusDot.State = e.IsSuccess ? StatusDotState.Ready : StatusDotState.Error;
                    navigationFailed = !e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled;
                    if (navigationFailed) Diagnostics.Log("navigation failed: " + e.WebErrorStatus);
                    RefreshPageNotice();
                    ApplyMediaState();
                    ApplyPageElementVisibility();
                    await DetectLiveStateAsync();
                };
                core.SourceChanged += async delegate
                {
                    if (isQuitting || IsDisposed) return;
                    ApplyPageElementVisibility();
                    await DetectLiveStateAsync();
                };
                core.IsMuted = !Visible || settings.Muted;
                webViewReady = true;
                UpdateZoom();
                if (Visible) liveTimer.Start();
                else ArmSuspendTimer(WebViewSuspendDelayMilliseconds);

                if (regressionTest)
                {
                    await RunRegressionAsync();
                }
                else if (liveSmokeTest)
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
                        try
                        {
                            using (Bitmap image = new Bitmap(ClientSize.Width, ClientSize.Height))
                            {
                                DrawToBitmap(image, new Rectangle(Point.Empty, ClientSize));
                                Directory.CreateDirectory(appFolder);
                                image.Save(Path.Combine(appFolder, "settings-fixture.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                        catch (Exception exception) { Console.Error.WriteLine("Settings fixture image: " + exception.Message); }
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
                Diagnostics.LogException("InitializeWebView", exception);
                if (isQuitting || IsDisposed) return;
                statusDot.State = StatusDotState.Error;
                if (IsTestMode || domProbe)
                {
                    Environment.ExitCode = 2;
                    QuitApplication();
                    return;
                }
                MessageBox.Show(this,
                    FailureMessages.ForWebView(exception) + "\r\n\r\n详细信息：" + exception.Message,
                    "波妞摸鱼无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RunDomProbe()
        {
            ObserveTask(RunDomProbeAsync(), "RunDomProbe");
        }

        private async Task RunDomProbeAsync()
        {
            string folder = System.IO.Path.GetTempPath();
            try
            {
                string clicked = await webView.ExecuteScriptAsync(PageScripts.OpenRecommendScript);
                Console.Error.WriteLine("open-recommend " + clicked);
                await Task.Delay(6000);
                string baseline = await webView.ExecuteScriptAsync(PageScripts.DomProbeScript);
                System.IO.File.WriteAllText(Path.Combine(folder, "boniu-dom.json"), baseline);
                DumpWindowShot(folder, "boniu-baseline.png");
                Console.Error.WriteLine("dom-probe baseline length=" + baseline.Length);

                settings.ImmersiveMode = true;
                toolbarRevealed = false;
                LayoutWindow();
                ApplyPageElementVisibility();
                await Task.Delay(4000);
                string immersive = await webView.ExecuteScriptAsync(PageScripts.DomProbeScript);
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
            catch (Exception exception)
            {
                Diagnostics.LogException("DumpWindowShot", exception);
            }
        }

    }
}
