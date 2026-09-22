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
        private void BuildSettingsPanel()
        {
            settingsPanel.BackColor = UiPalette.Page;
            settingsPanel.Visible = false;

            headingLabel.Text = "设置";
            headingLabel.Font = UiFonts.Hero;
            headingLabel.ForeColor = UiPalette.TextPrimary;
            headingLabel.BackColor = UiPalette.Page;
            headingLabel.AutoSize = true;
            headingLabel.MouseDown += DragWindow;
            settingsPanel.Controls.Add(headingLabel);

            brandLabel.Text = "波妞摸鱼";
            brandLabel.Font = UiFonts.Caption;
            brandLabel.ForeColor = UiPalette.TextSecondary;
            brandLabel.BackColor = UiPalette.Page;
            brandLabel.AutoSize = true;
            brandLabel.MouseDown += DragWindow;
            settingsPanel.Controls.Add(brandLabel);

            SettingsCard hotkeyCard = AddCard("快捷键");
            AddInlineRow(hotkeyCard, "隐藏/显示程序", windowShortcutStatus, windowShortcutButton, 104, 30);
            AddInlineRow(hotkeyCard, "隐藏/显示边框", chromeShortcutStatus, chromeShortcutButton, 104, 30);
            AddInlineRow(hotkeyCard, "清爽模式", immersiveShortcutStatus, immersiveShortcutButton, 104, 30);
            AddInlineRow(hotkeyCard, "静音/恢复声音", muteShortcutStatus, muteShortcutButton, 104, 30);

            SettingsCard behaviorCard = AddCard("行为");
            AddInlineRow(behaviorCard, "鼠标移出自动隐藏", autoHideStatus, autoHideToggle, 44, 26);
            delayRow = AddStackedRow(behaviorCard, "隐藏延迟", autoHideDelayPicker, 28);
            AddInlineRow(behaviorCard, "失去焦点时隐藏", inactiveHideStatus, inactiveHideToggle, 44, 26);
            AddInlineRow(behaviorCard, "直播自动横屏", liveStatus, landscapeToggle, 44, 26);
            AddInlineRow(behaviorCard, "显示托盘恢复入口", trayStatus, trayToggle, 44, 26);
            trayToggle.CheckedChanged += delegate
            {
                if (settings.ShowTrayIcon == trayToggle.Checked) return;
                settings.ShowTrayIcon = trayToggle.Checked;
                UpdateRecoveryTray();
                ScheduleSettingsSave();
                UpdateSettingsUi();
            };

            SettingsCard interfaceCard = AddCard("界面");
            AddInlineRow(interfaceCard, "隐藏左侧导航栏", leftNavStatus, leftNavToggle, 44, 26);
            AddInlineRow(interfaceCard, "隐藏顶部搜索栏", topBarStatus, topBarToggle, 44, 26);
            AddInlineRow(interfaceCard, "隐藏右侧互动区", rightBarStatus, rightBarToggle, 44, 26);
            AddInlineRow(interfaceCard, "清爽模式（保留原生播放器）", immersiveStatus, immersiveToggle, 44, 26);
            AddFullWidthRow(interfaceCard, retryPageButton, 36);
            retryPageButton.Click += delegate { ReapplyPageStyles(); };

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

            autoHideDelayPicker.Font = UiFonts.Caption;
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
                pageStyleFallback = false;
                settings.ImmersiveMode = immersiveToggle.Checked;
                RefreshPageNotice();
                Diagnostics.Log("immersive=" + settings.ImmersiveMode + " source=settings-switch");
                toolbarRevealed = false;
                ScheduleSettingsSave();
                LayoutWindow();
                UpdateSettingsUi();
                ApplyPageElementVisibility();
            };
            immersiveShortcutButton.Click += delegate { BeginShortcutCapture("immersive"); };
            muteShortcutButton.Click += delegate { BeginShortcutCapture("mute"); };
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
            card.TitleLabel.Font = UiFonts.Caption;
            card.TitleLabel.ForeColor = UiPalette.TextSecondary;
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
                row.TitleLabel.Font = UiFonts.Body;
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
            // AutoSize=false used to retain a wrapped/default height (23px), spilling into
            // the next row at small window widths. Both title and status are single-line.
            Size measured = TextRenderer.MeasureText(string.IsNullOrEmpty(label.Text) ? " " : label.Text,
                label.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine);
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Size = new Size(Math.Max(1, Math.Min(Math.Max(1, maxWidth), measured.Width)), measured.Height);
        }

        private void SetDataStatus(string message)
        {
            dataStatus.Text = message;
            LayoutSettingsPanel();
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
                if (button == null || !button.Visible) continue;
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

        private bool FitsInsideCard(SettingsCard card, Control control)
        {
            if (control == null || !control.Visible) return true;
            // AutoScroll 会把滚动偏移烘焙进子控件 Location（客户坐标），而 card.Bounds 是
            // 文档坐标；统一换算到文档空间再比较，检查结果与滚动位置无关。
            int scrollY = settingsPanel.ScrollOffsetY;
            Rectangle bounds = new Rectangle(control.Left, control.Top + scrollY, control.Width, control.Height);
            bool fits = bounds.Left >= card.Bounds.Left && bounds.Right <= card.Bounds.Right
                && bounds.Top >= card.Bounds.Top && bounds.Bottom <= card.Bounds.Bottom;
            if (!fits) Console.Error.WriteLine("settings overflow: text={0} bounds={1} card={2}", control.Text, bounds, card.Bounds);
            return fits;
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
            if (danger) button.BorderColor = UiPalette.Destructive;
            button.Font = UiFonts.Caption;
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
            windowHotkey = ParseShortcut(settings.HideShortcut, Keys.D);
            chromeHotkey = ParseShortcut(settings.ChromeShortcut, Keys.B);
            muteHotkey = ParseShortcut(settings.MuteShortcut, Keys.M);
            immersiveHotkey = ParseShortcut(settings.ImmersiveShortcut, Keys.F);
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
            // 行内横幅替代模态对话框（老板键安全）：显示文本自动省略，tooltip 保留全文。
            ShowTransientNotice(message.Replace("\r\n", " ").Replace("\n", " "));
        }

        private void UpdateSettingsUi()
        {
            UpdateRecoveryTray();
            trayToggle.Checked = settings.ShowTrayIcon;
            trayStatus.Text = !windowHotkeyAvailable && IsHandleCreated ? "快捷键不可用时自动保留恢复入口" : settings.ShowTrayIcon ? "双击托盘图标恢复窗口" : "关闭；仍可重新打开程序恢复";
            settings.AutoHideDelayMilliseconds = WindowRules.NormalizeAutoHideDelay(settings.AutoHideDelayMilliseconds);
            windowShortcutButton.Text = ShortcutButtonText(windowHotkey);
            chromeShortcutButton.Text = ShortcutButtonText(chromeHotkey);
            muteShortcutButton.Text = ShortcutButtonText(muteHotkey);
            immersiveShortcutButton.Text = ShortcutButtonText(immersiveHotkey);
            windowShortcutButton.FaceColor = UiPalette.ShortcutFace;
            chromeShortcutButton.FaceColor = UiPalette.ShortcutFace;
            muteShortcutButton.FaceColor = UiPalette.ShortcutFace;
            immersiveShortcutButton.FaceColor = UiPalette.ShortcutFace;
            windowShortcutStatus.Text = ShortcutStatusText(windowHotkeyAvailable || !IsHandleCreated, windowHotkey);
            chromeShortcutStatus.Text = ShortcutStatusText(chromeHotkeyAvailable || !IsHandleCreated, chromeHotkey);
            muteShortcutStatus.Text = ShortcutStatusText(muteHotkeyAvailable || !IsHandleCreated, muteHotkey);
            immersiveShortcutStatus.Text = ShortcutStatusText(immersiveHotkeyAvailable || !IsHandleCreated, immersiveHotkey);
            toolTip.SetToolTip(windowShortcutButton, "隐藏/显示程序" + FormatShortcut(windowHotkey) + "；" + ShortcutHint(windowHotkey));
            toolTip.SetToolTip(chromeShortcutButton, "隐藏/显示边框" + FormatShortcut(chromeHotkey) + "；" + ShortcutHint(chromeHotkey));
            toolTip.SetToolTip(muteShortcutButton, "静音/恢复声音" + FormatShortcut(muteHotkey) + "；" + ShortcutHint(muteHotkey));
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
            toolTip.SetToolTip(immersiveShortcutButton,
                "进入/退出清爽模式" + FormatShortcut(immersiveHotkey) + "；" + ShortcutHint(immersiveHotkey));
            immersiveToggle.Checked = settings.ImmersiveMode;
            immersiveStatus.Text = pageStyleFallback ? "布局异常，已回退；可点击重新应用" : settings.ImmersiveMode
                ? "隐藏页面装饰，保留原生播放器" : "已关闭";
            LayoutSettingsPanel();
            UpdateToolbarState();
        }

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
}
