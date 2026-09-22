using System;
using System.Drawing;
using System.Windows.Forms;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private NotifyIcon recoveryIcon;
        private ContextMenuStrip recoveryMenu;
        private Icon recoveryIconImage;
        private bool CanRestoreWindow { get { return windowHotkeyAvailable || (recoveryIcon != null && recoveryIcon.Visible); } }

        private void InitializeRecoveryTray()
        {
            if (IsTestMode || recoveryIcon != null) return;
            recoveryIconImage = Icon == null ? (Icon)SystemIcons.Application.Clone() : (Icon)Icon.Clone();
            recoveryMenu = new ContextMenuStrip();
            recoveryMenu.Items.Add("显示窗口", null, delegate { ShowWindow(); });
            recoveryMenu.Items.Add("隐藏窗口", null, delegate { HideWindowSafely(); });
            recoveryMenu.Items.Add("恢复工具栏并打开设置", null, delegate
            {
                ShowWindow();
                ApplyChromeState(false);
                if (!settingsOpen) ToggleSettings();
            });
            recoveryMenu.Items.Add(new ToolStripSeparator());
            recoveryMenu.Items.Add("退出", null, delegate { QuitApplication(); });
            recoveryMenu.Opening += delegate { suppressInactiveHide = true; };
            recoveryMenu.Closed += delegate
            {
                suppressInactiveHide = false;
                showGraceUntil = DateTime.UtcNow.AddMilliseconds(ShowGraceMilliseconds);
            };
            recoveryIcon = new NotifyIcon();
            recoveryIcon.Icon = recoveryIconImage;
            recoveryIcon.Text = "波妞摸鱼 · 双击恢复窗口";
            recoveryIcon.ContextMenuStrip = recoveryMenu;
            recoveryIcon.DoubleClick += delegate { ShowWindow(); };
            UpdateRecoveryTray();
        }

        private void UpdateRecoveryTray()
        {
            if (recoveryIcon != null) recoveryIcon.Visible = !isQuitting && (settings.ShowTrayIcon || !windowHotkeyAvailable);
        }

        private void DisposeRecoveryTray()
        {
            if (recoveryIcon != null) { recoveryIcon.Visible = false; recoveryIcon.Dispose(); recoveryIcon = null; }
            if (recoveryMenu != null) recoveryMenu.Dispose();
            if (recoveryIconImage != null) recoveryIconImage.Dispose();
        }
    }
}
