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
            status.Text = "正在录入，Esc 取消 · Del 禁用";
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
            if ((e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
                && !e.Control && !e.Alt && !e.Shift)
            {
                DisableCapturedShortcut();
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
            else if (captureTarget == "mute") muteHotkey = candidate;
            else immersiveHotkey = candidate;
            SetHotkeyAvailable(captureTarget, true);
            PersistShortcut(captureTarget, candidate);
            captureTarget = null;
            ScheduleSettingsSave();
            UpdateSettingsUi();
        }

        private void DisableCapturedShortcut()
        {
            string target = captureTarget;
            captureTarget = null;
            UnregisterTargetHotkey(target);
            if (target == "window") windowHotkey = null;
            else if (target == "chrome") chromeHotkey = null;
            else if (target == "mute") muteHotkey = null;
            else immersiveHotkey = null;
            PersistShortcut(target, null);
            ScheduleSettingsSave();
            UpdateSettingsUi();
        }

        private RoundedButton ShortcutButtonFor(string target)
        {
            if (target == "window") return windowShortcutButton;
            if (target == "chrome") return chromeShortcutButton;
            return target == "mute" ? muteShortcutButton : immersiveShortcutButton;
        }

        private Label ShortcutStatusFor(string target)
        {
            if (target == "window") return windowShortcutStatus;
            if (target == "chrome") return chromeShortcutStatus;
            return target == "mute" ? muteShortcutStatus : immersiveShortcutStatus;
        }

        private int HotkeyIdFor(string target)
        {
            if (target == "window") return HotkeyWindow;
            if (target == "chrome") return HotkeyChrome;
            return target == "mute" ? HotkeyMute : HotkeyImmersive;
        }

        private HotkeyDefinition HotkeyFor(string target)
        {
            if (target == "window") return windowHotkey;
            if (target == "chrome") return chromeHotkey;
            return target == "mute" ? muteHotkey : immersiveHotkey;
        }

        private void SetHotkeyAvailable(string target, bool available)
        {
            if (target == "window") windowHotkeyAvailable = available;
            else if (target == "chrome") chromeHotkeyAvailable = available;
            else if (target == "mute") muteHotkeyAvailable = available;
            else immersiveHotkeyAvailable = available;
        }

        private void PersistShortcut(string target, HotkeyDefinition definition)
        {
            string value = definition == null ? "" : definition.Serialize();
            if (target == "window") settings.HideShortcut = value;
            else if (target == "chrome") settings.ChromeShortcut = value;
            else if (target == "mute") settings.MuteShortcut = value;
            else settings.ImmersiveShortcut = value;
        }

        private bool ConflictsWithAnyRegistered(string target, HotkeyDefinition candidate)
        {
            string[] others = { "window", "chrome", "mute", "immersive" };
            foreach (string other in others)
            {
                if (other == target) continue;
                if (candidate.ConflictsWith(HotkeyFor(other))) return true;
            }
            return false;
        }

        private static HotkeyDefinition ParseShortcut(string value, Keys fallbackKey)
        {
            if (value != null && value.Trim().Length == 0) return null;
            HotkeyDefinition parsed;
            return HotkeyDefinition.TryParse(value, out parsed)
                ? parsed : new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, fallbackKey);
        }

        private static string FormatShortcut(HotkeyDefinition definition)
        {
            return definition == null ? "" : " (" + definition.Display() + ")";
        }

        private static string ShortcutHint(HotkeyDefinition definition)
        {
            return definition == null ? "点击录入" : "点击修改";
        }

        private static string ShortcutButtonText(HotkeyDefinition definition)
        {
            return definition == null ? "未设置" : definition.Display();
        }

        private static string ShortcutStatusText(bool available, HotkeyDefinition definition)
        {
            return available ? ShortcutHint(definition) : "快捷键不可用，请重新设置";
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
            UpdateRecoveryTray();
            string warnings = "";
            if (windowHotkey != null && !windowHotkeyAvailable)
                warnings += windowHotkey.Display() + " 已被占用：已停用鼠标移出自动隐藏并保留托盘恢复入口，可重新打开程序恢复窗口。 ";
            if (chromeHotkey != null && !chromeHotkeyAvailable)
            {
                ApplyChromeState(false);
                warnings += chromeHotkey.Display() + " 已被占用：无边框模式保持关闭，请在设置中更换快捷键。 ";
            }
            if (muteHotkey != null && !muteHotkeyAvailable)
                warnings += muteHotkey.Display() + " 已被占用：静音键本次不可用，请在设置中更换快捷键。 ";
            if (immersiveHotkey != null && !immersiveHotkeyAvailable)
                warnings += immersiveHotkey.Display() + " 已被占用：清爽模式本次不可用，请在设置中更换快捷键。 ";
            if (warnings.Length > 0) ShowTransientNotice(warnings.Trim());
            UpdateSettingsUi();
        }

        private bool RegisterHotkey(int id, HotkeyDefinition definition)
        {
            NativeMethods.UnregisterHotKey(Handle, id);
            if (definition == null) return false;
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

    }
}
