using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MiniView.WebView2App
{
    /// <summary>窗口贴靠的屏幕边缘。只支持左右两边：上边会和清爽模式的贴顶唤出工具栏抢同一块区域。</summary>
    internal enum EdgeDockSide
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    internal static class WindowRules
    {
        internal static readonly Size DefaultSize = new Size(420, 760);
        internal static readonly Size MiniSize = new Size(340, 600);
        internal const int MiniMinWidth = 260;
        internal const int MiniMinHeight = 300;
        // 贴边自动隐藏：拖动结束后距屏幕边缘这个范围内算贴边；收起后鼠标贴在边缘这条窄带内唤出；
        // 触发段按窗口原本占据的高度上下各留一点容错；鼠标离开后等一小会儿再收起，避免蹭边反复起停。
        internal const int EdgeDockThreshold = 16;
        internal const int EdgeDockBand = 4;
        internal const int EdgeDockSpanPadding = 8;
        internal const int EdgeDockLeaveDelayMilliseconds = 300;

        internal static bool IsDouyinUrl(string rawUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            string host = uri.DnsSafeHost;
            return string.Equals(host, "douyin.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".douyin.com", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsLiveUrl(string rawUrl)
        {
            Uri uri;
            if (!IsDouyinUrl(rawUrl) || !Uri.TryCreate(rawUrl, UriKind.Absolute, out uri)) return false;
            return string.Equals(uri.DnsSafeHost, "live.douyin.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.AbsolutePath, "/live", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase);
        }

        internal static double CalculateZoomFactor(int width)
        {
            int compactWidth = Math.Max(280, width);
            double factor = Math.Min(1.0, Math.Max(0.44, compactWidth / 726.0));
            return Math.Round(factor, 2, MidpointRounding.AwayFromZero);
        }

        internal static int NormalizeAutoHideDelay(int value)
        {
            int[] supported = { 100, 300, 500 };
            return supported.Contains(value) ? value : 100;
        }

        /// <summary>
        /// 清爽模式顶部悬停是否显示工具栏。带迟滞：贴顶 revealHeight 以内唤出，已经唤出时
        /// 光标留在 keepHeight 以内就保持，离开才收起——否则工具栏一弹出视频被推下去，
        /// 指针往下移一点就立刻满足收起条件，会来回抖动且点不到按钮。
        /// 光标略高于窗口上边缘（-keepHeight 到 0）也算贴顶：采样是 10ms 一次，鼠标上甩时
        /// 两次采样之间可能跨过 20~40 像素，没有这条兜底就会整个跳过窄带、怎么甩都不出来。
        /// 上限同样收在 keepHeight 内，避免鼠标停在窗口上方别的窗口上时工具栏一直挂着。
        /// </summary>
        internal static bool ShouldRevealToolbar(int cursorX, int cursorY, int clientWidth,
            bool currentlyRevealed, int revealHeight, int keepHeight)
        {
            if (cursorX < 0 || cursorX > clientWidth) return false;
            if (cursorY <= revealHeight && cursorY >= -keepHeight) return true;
            return currentlyRevealed && cursorY <= keepHeight && cursorY >= -keepHeight;
        }

        /// <summary>
        /// 顶部悬停临时唤出的工具栏是否以浮层覆盖在播放区上方（不占布局高度）。
        /// 只有"清爽模式下工具栏本来收起、因为悬停才露出来"这一种情况算浮层：此时播放区
        /// 在收起与唤出的两种状态下尺寸完全相同，视频不会被推下去，网页也不会收到 resize
        /// 而重排。正常模式和设置页里的工具栏是常驻布局的一部分，仍然占位，避免长期盖住画面。
        /// </summary>
        internal static bool ShouldOverlayToolbar(bool immersive, bool chromeHidden, bool settingsOpen, bool revealed)
        {
            return immersive && !chromeHidden && !settingsOpen && revealed;
        }

        internal static Rectangle KeepOnScreen(BoundsData saved, int minWidth, int minHeight, Size fallback)
        {
            Rectangle[] workAreas = Screen.AllScreens.Select(delegate(Screen screen) { return screen.WorkingArea; }).ToArray();
            int width = Math.Max(minWidth, saved == null ? fallback.Width : saved.Width);
            int height = Math.Max(minHeight, saved == null ? fallback.Height : saved.Height);
            width = Math.Min(width, workAreas.Max(delegate(Rectangle area) { return area.Width; }));
            height = Math.Min(height, workAreas.Max(delegate(Rectangle area) { return area.Height; }));

            if (saved != null)
            {
                foreach (Rectangle area in workAreas)
                {
                    Point probe = new Point(saved.X + 30, saved.Y + 30);
                    if (!area.Contains(probe)) continue;
                    int x = Math.Max(area.Left, Math.Min(saved.X, area.Right - 120));
                    int y = Math.Max(area.Top, Math.Min(saved.Y, area.Bottom - 80));
                    return new Rectangle(x, y, Math.Min(width, area.Width), Math.Min(height, area.Height));
                }
            }

            Rectangle primary = Screen.PrimaryScreen.WorkingArea;
            width = Math.Min(width, primary.Width);
            height = Math.Min(height, primary.Height);
            return new Rectangle(
                primary.Left + (primary.Width - width) / 2,
                primary.Top + (primary.Height - height) / 2,
                width,
                height);
        }

        internal static Rectangle CalculateLandscapeBounds(Rectangle current, Rectangle workArea)
        {
            int width = Math.Min(workArea.Width, Math.Max(640, Math.Max(current.Height, current.Width)));
            int height = Math.Min(workArea.Height, Math.Max(360, (int)Math.Round(width * 9.0 / 16.0)));
            double centerX = current.Left + current.Width / 2.0;
            double centerY = current.Top + current.Height / 2.0;
            int x = (int)Math.Round(Math.Max(workArea.Left, Math.Min(centerX - width / 2.0, workArea.Right - width)));
            int y = (int)Math.Round(Math.Max(workArea.Top, Math.Min(centerY - height / 2.0, workArea.Bottom - height)));
            return new Rectangle(x, y, width, height);
        }

        internal static Rectangle CalculateCornerBounds(Rectangle workArea, Size size, int margin)
        {
            int width = Math.Min(Math.Max(MiniMinWidth, size.Width), workArea.Width);
            int height = Math.Min(Math.Max(MiniMinHeight, size.Height), workArea.Height);
            return new Rectangle(
                Math.Max(workArea.Left, workArea.Right - width - margin),
                Math.Max(workArea.Top, workArea.Bottom - height - margin),
                width,
                height);
        }

        /// <summary>
        /// 贴边自动隐藏开启时，鼠标移出自动隐藏必须让位：两套逻辑都靠"鼠标离开窗口"触发，
        /// 同时生效时旧的隐藏会抢先真正隐藏窗口，贴边收起和碰边唤出就永远不会发生。
        /// </summary>
        internal static bool EffectiveMouseLeaveAutoHide(bool mouseLeaveEnabled, bool edgeAutoHideEnabled)
        {
            return mouseLeaveEnabled && !edgeAutoHideEnabled;
        }

        /// <summary>
        /// 拖动结束后判断窗口贴到了哪条屏幕边缘。"贴到"和"冲过头"都算：拖动时窗口右边缘 =
        /// 光标位置 + (窗口宽度 − 抓标题栏的位置)，抓标题栏中间再往屏幕右边拖，窗口右边缘会比
        /// 光标再多出半个窗宽，所以用绝对值判定时冲过头的窗口会被判成"没贴边"——既收不起也唤不回。
        /// </summary>
        internal static EdgeDockSide ResolveEdgeDockSide(Rectangle bounds, Rectangle workArea, int threshold)
        {
            int leftDistance = Math.Abs(bounds.Left - workArea.Left);
            int rightDistance = Math.Abs(bounds.Right - workArea.Right);
            bool left = bounds.Left <= workArea.Left + threshold;
            bool right = bounds.Right >= workArea.Right - threshold;
            if (left && right) return leftDistance <= rightDistance ? EdgeDockSide.Left : EdgeDockSide.Right;
            if (left) return EdgeDockSide.Left;
            if (right) return EdgeDockSide.Right;
            return EdgeDockSide.None;
        }

        /// <summary>
        /// 拖动过程中的左右吸附：靠近屏幕边缘就贴齐过去，并且不允许继续往屏幕外的方向移动。
        /// 只处理左右两侧（上下不做限制）。反向拖动一律立刻放手，不给"离开"再设一个阈值，
        /// 否则窗口贴在边上时会先黏住一小段才肯跟着走。纵向完全不动——吸边是横向的事。
        /// </summary>
        internal static Rectangle SnapSideEdges(Rectangle proposed, Rectangle current, Rectangle workArea, int approach)
        {
            // 窗口比工作区还宽时没有可吸附的余地，原样返回，免得硬把它挤到某一侧。
            if (proposed.Width >= workArea.Width) return proposed;

            bool left = proposed.Left <= workArea.Left + approach && proposed.Left <= current.Left;
            bool right = proposed.Right >= workArea.Right - approach && proposed.Right >= current.Right;
            if (left && right)
            {
                // 窗口几乎和工作区一样宽时两侧会同时命中，取更靠近的那侧，结果才稳定。
                if (Math.Abs(proposed.Left - workArea.Left) <= Math.Abs(proposed.Right - workArea.Right)) right = false;
                else left = false;
            }
            if (left) proposed.X = workArea.Left;
            else if (right) proposed.X = workArea.Right - proposed.Width;
            return proposed;
        }

        /// <summary>把窗口贴齐到该侧屏幕边缘，保留大小，纵向位置夹在工作区内。</summary>
        internal static Rectangle CalculateEdgeDockBounds(Rectangle workArea, Rectangle current, EdgeDockSide side)
        {
            int width = Math.Min(Math.Max(1, current.Width), workArea.Width);
            int height = Math.Min(Math.Max(1, current.Height), workArea.Height);
            int y = Math.Max(workArea.Top, Math.Min(current.Top, workArea.Bottom - height));
            int x = side == EdgeDockSide.Right ? workArea.Right - width : workArea.Left;
            return new Rectangle(x, y, width, height);
        }

        /// <summary>
        /// 收起后鼠标回到屏幕边缘的哪一段才唤出：横向是贴着该侧边缘的窄带，
        /// 纵向只覆盖窗口原本占据的那一段（上下各留一点容错），不整条边都变成热区。
        /// </summary>
        internal static Rectangle CalculateEdgeRevealZone(Rectangle anchorBounds, Rectangle workArea,
            EdgeDockSide side, int band, int padding)
        {
            if (side == EdgeDockSide.None) return Rectangle.Empty;
            int top = Math.Max(workArea.Top, anchorBounds.Top - padding);
            int bottom = Math.Min(workArea.Bottom, anchorBounds.Bottom + padding);
            if (bottom <= top) return Rectangle.Empty;
            int left = side == EdgeDockSide.Left ? workArea.Left : workArea.Right - band;
            return new Rectangle(left, top, band, bottom - top);
        }

        /// <summary>收起状态下判断是否应当滑回来：必须先离开过触发段，否则刚拖到边缘就会立刻弹回。</summary>
        internal static bool ShouldRevealDockedWindow(Point cursor, Rectangle revealZone, bool revealArmed)
        {
            return revealArmed && revealZone.Contains(cursor);
        }
    }

    [Flags]
    internal enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000
    }

    internal sealed class HotkeyDefinition
    {
        internal HotkeyModifiers Modifiers { get; private set; }
        internal Keys Key { get; private set; }

        internal HotkeyDefinition(HotkeyModifiers modifiers, Keys key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        internal string Serialize()
        {
            List<string> parts = new List<string>();
            if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Control");
            if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Super");
            parts.Add(KeyName(Key));
            return string.Join("+", parts.ToArray());
        }

        internal string Display()
        {
            return Serialize().Replace("Control", "Ctrl").Replace("Super", "Win").Replace("+", " + ");
        }

        internal uint NativeModifiers
        {
            get { return (uint)(Modifiers | HotkeyModifiers.NoRepeat); }
        }

        internal bool ConflictsWith(HotkeyDefinition other)
        {
            if (other == null) return false;
            HotkeyModifiers mask = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win;
            return (Modifiers & mask) == (other.Modifiers & mask) && Key == other.Key;
        }

        internal static HotkeyDefinition FromKeyEvent(KeyEventArgs e)
        {
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if (e.Control) modifiers |= HotkeyModifiers.Control;
            if (e.Alt) modifiers |= HotkeyModifiers.Alt;
            if (e.Shift) modifiers |= HotkeyModifiers.Shift;
            if ((NativeMethods.GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0)
            {
                modifiers |= HotkeyModifiers.Win;
            }

            Keys key = e.KeyCode;
            if (modifiers == HotkeyModifiers.None || IsModifierKey(key) || !IsSupportedKey(key)) return null;
            return new HotkeyDefinition(modifiers, key);
        }

        internal static bool TryParse(string value, out HotkeyDefinition result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            Keys key = Keys.None;
            int keyCount = 0;

            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Equals("Control", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("CommandOrControl", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyModifiers.Control;
                }
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= HotkeyModifiers.Alt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= HotkeyModifiers.Shift;
                else if (part.Equals("Super", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Win", StringComparison.OrdinalIgnoreCase)) modifiers |= HotkeyModifiers.Win;
                else
                {
                    Keys parsed;
                    if (!TryParseKey(part, out parsed)) return false;
                    key = parsed;
                    keyCount++;
                }
            }

            if (modifiers == HotkeyModifiers.None || keyCount != 1 || !IsSupportedKey(key)) return false;
            result = new HotkeyDefinition(modifiers, key);
            return !result.IsReservedByWindows();
        }

        internal bool IsReservedByWindows()
        {
            HotkeyDefinition reserved;
            string[] reservedValues = { "Alt+F4", "Control+Alt+Delete", "Control+Shift+Escape", "Super+L" };
            foreach (string value in reservedValues)
            {
                if (TryParseIgnoringReserved(value, out reserved) && ConflictsWith(reserved)) return true;
            }
            return false;
        }

        private static bool TryParseIgnoringReserved(string value, out HotkeyDefinition result)
        {
            result = null;
            string[] parts = value.Split('+');
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            Keys key = Keys.None;
            foreach (string part in parts)
            {
                if (part == "Control") modifiers |= HotkeyModifiers.Control;
                else if (part == "Alt") modifiers |= HotkeyModifiers.Alt;
                else if (part == "Shift") modifiers |= HotkeyModifiers.Shift;
                else if (part == "Super") modifiers |= HotkeyModifiers.Win;
                else if (!TryParseKey(part, out key)) return false;
            }
            result = new HotkeyDefinition(modifiers, key);
            return true;
        }

        private static bool TryParseKey(string value, out Keys key)
        {
            key = Keys.None;
            string normalized = value.Trim();
            if (normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]))
            {
                key = (Keys)char.ToUpperInvariant(normalized[0]);
                return true;
            }
            if (normalized.Equals("Space", StringComparison.OrdinalIgnoreCase)) normalized = "Space";
            if (normalized.Equals("Left", StringComparison.OrdinalIgnoreCase)) normalized = "Left";
            if (normalized.Equals("Right", StringComparison.OrdinalIgnoreCase)) normalized = "Right";
            if (normalized.Equals("Up", StringComparison.OrdinalIgnoreCase)) normalized = "Up";
            if (normalized.Equals("Down", StringComparison.OrdinalIgnoreCase)) normalized = "Down";
            return Enum.TryParse<Keys>(normalized, true, out key);
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey
                || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu
                || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey
                || key == Keys.LWin || key == Keys.RWin;
        }

        private static bool IsSupportedKey(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z) return true;
            if (key >= Keys.D0 && key <= Keys.D9) return true;
            if (key >= Keys.F1 && key <= Keys.F24) return true;
            Keys[] named = {
                Keys.Back, Keys.Delete, Keys.Down, Keys.End, Keys.Enter, Keys.Escape,
                Keys.Home, Keys.Insert, Keys.Left, Keys.PageDown, Keys.PageUp,
                Keys.Right, Keys.Space, Keys.Tab, Keys.Up
            };
            return named.Contains(key);
        }

        private static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);
            if (key == Keys.Back) return "Backspace";
            return key.ToString();
        }
    }
}
