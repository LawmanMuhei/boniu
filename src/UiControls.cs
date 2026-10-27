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
    internal static class UiPalette
    {
        internal static readonly Color Page = Color.FromArgb(14, 15, 20);
        internal static readonly Color Card = Color.FromArgb(28, 30, 40);
        internal static readonly Color CardElevated = Color.FromArgb(36, 39, 52);
        internal static readonly Color Separator = Color.FromArgb(44, 47, 61);
        internal static readonly Color TextPrimary = Color.FromArgb(242, 243, 247);
        internal static readonly Color TextSecondary = Color.FromArgb(164, 168, 184);
        internal static readonly Color TextTertiary = Color.FromArgb(113, 117, 137);
        internal static readonly Color Accent = Color.FromArgb(123, 114, 240);
        internal static readonly Color AccentHover = Color.FromArgb(142, 134, 245);
        internal static readonly Color AccentPressed = Color.FromArgb(98, 88, 223);
        internal static readonly Color Highlight = Color.FromArgb(255, 190, 69);
        internal static readonly Color Ink = Color.FromArgb(66, 72, 92);
        internal static readonly Color InkHover = Color.FromArgb(80, 86, 106);
        internal static readonly Color InkPressed = Color.FromArgb(96, 102, 122);
        internal static readonly Color Destructive = Color.FromArgb(255, 90, 95);
        internal static readonly Color Shell = Color.FromArgb(20, 21, 28);
        internal static readonly Color Toolbar = Color.FromArgb(24, 26, 34);
        internal static readonly Color ToolbarOverlay = Color.FromArgb(30, 32, 41);
        internal static readonly Color ToolbarGlyph = TextPrimary;
        internal static readonly Color ToolbarHover = Color.FromArgb(44, 47, 61);
        internal static readonly Color ToolbarPressed = Color.FromArgb(57, 61, 78);
        internal static readonly Color ToolbarActive = Color.FromArgb(45, 42, 85);
        internal static readonly Color ToolbarActiveGlyph = Color.FromArgb(185, 179, 255);
        internal static readonly Color CloseHover = Color.FromArgb(229, 72, 77);
        internal static readonly Color ClosePressed = Color.FromArgb(199, 54, 60);
        internal static readonly Color ButtonFace = CardElevated;
        internal static readonly Color ShortcutFace = CardElevated;
        internal static readonly Color ShortcutCapturing = CardElevated;
    }

    internal static class UiFonts
    {
        private static readonly FontFamily family = ResolveFamily();
        internal static readonly Font Hero = new Font("Segoe UI Semibold", 18F);
        internal static readonly Font Toolbar = new Font(ResolveIconFamily(), 10.5F);
        internal static readonly Font Body = new Font(family, 12F);
        internal static readonly Font Caption = new Font(family, 11F);
        internal static readonly Font Micro = new Font(family, 9F);

        private static FontFamily ResolveIconFamily()
        {
            foreach (FontFamily candidate in FontFamily.Families)
                if (candidate.Name == "Segoe Fluent Icons") return candidate;
            return new FontFamily("Segoe MDL2 Assets");
        }

        private static FontFamily ResolveFamily()
        {
            try
            {
                FontFamily system = SystemFonts.MessageBoxFont.FontFamily;
                if (system != null) return system;
            }
            catch { }
            try { return new FontFamily("Microsoft YaHei UI"); }
            catch { return FontFamily.GenericSansSerif; }
        }
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

        internal static void DrawFocusRing(Graphics graphics, Rectangle bounds)
        {
            RectangleF inset = new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4);
            using (GraphicsPath path = RoundedPath(inset, 6))
            using (Pen pen = new Pen(UiPalette.Accent, 1.5F)) graphics.DrawPath(pen, path);
        }
    }

    // 工具栏按钮共用一个时钟，只刷新正在过渡的按钮，不为每个控件创建计时器。
    internal static class ButtonAnimationClock
    {
        private static readonly List<RoundedButton> pending = new List<RoundedButton>();
        private static readonly Timer timer = new Timer();

        static ButtonAnimationClock()
        {
            timer.Interval = 15;
            timer.Tick += delegate
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    RoundedButton button = pending[i];
                    if (button.IsDisposed) { pending.RemoveAt(i); continue; }
                    button.Invalidate();
                    if (!button.AnimationRunning) pending.RemoveAt(i);
                }
                if (pending.Count == 0) timer.Stop();
            };
        }

        internal static void Start(RoundedButton button)
        {
            if (!pending.Contains(button)) pending.Add(button);
            timer.Start();
        }

        internal static void Remove(RoundedButton button)
        {
            pending.Remove(button);
            if (pending.Count == 0) timer.Stop();
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;
        private bool active;
        private Color faceColor = UiPalette.ButtonFace;
        private Color transitionFrom;
        private Color transitionTo;
        private DateTime transitionStarted;
        internal bool AnimateColors { get; set; }
        internal bool AccentStripe { get; set; }
        internal bool AnimationRunning { get { return (DateTime.UtcNow - transitionStarted).TotalMilliseconds < 120; } }

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

        internal Color BorderColor { get; set; }

        internal bool Active
        {
            get { return active; }
            set { if (active == value) return; StartTransition(); active = value; FinishTransition(); }
        }

        internal int CornerRadius { get; set; }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); StartTransition(); hovered = true; FinishTransition(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); StartTransition(); hovered = false; pressed = false; FinishTransition(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            StartTransition(); pressed = true; FinishTransition();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); StartTransition(); pressed = false; FinishTransition(); }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color face = AnimatedFace();
            Color text = ResolveFore();

            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            if (face.A > 0)
            {
                using (GraphicsPath path = UiPaint.RoundedPath(rect, CornerRadius))
                using (SolidBrush brush = new SolidBrush(face))
                    e.Graphics.FillPath(brush, path);
            }
            if (!BorderColor.IsEmpty)
            {
                using (GraphicsPath path = UiPaint.RoundedPath(rect, CornerRadius))
                using (Pen pen = new Pen(BorderColor, 1F))
                    e.Graphics.DrawPath(pen, path);
            }
            if (AccentStripe)
                using (Pen pen = new Pen(UiPalette.Accent, 3F))
                    e.Graphics.DrawLine(pen, 3, 9, 3, Math.Max(9, Height - 9));
            if (Focused && ShowFocusCues) UiPaint.DrawFocusRing(e.Graphics, ClientRectangle);
            if (Text.Length == 0) return;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private Color ResolveFace()
        {
            if (!Enabled) return UiPaint.Blend(faceColor, BackColor, 0.55F);
            if (active && !ActiveFaceColor.IsEmpty)
                return pressed ? UiPaint.Blend(ActiveFaceColor, Color.White, 0.16F)
                    : hovered ? UiPaint.Blend(ActiveFaceColor, Color.White, 0.08F) : ActiveFaceColor;
            if (pressed) return PressedFaceColor.IsEmpty
                ? UiPaint.Blend(faceColor, Color.White, 0.22F) : PressedFaceColor;
            if (hovered) return HoverFaceColor.IsEmpty
                ? UiPaint.Blend(faceColor, Color.White, 0.12F) : HoverFaceColor;
            return faceColor;
        }

        private Color ResolveFore()
        {
            Color fore = active && !ActiveForeColor.IsEmpty ? ActiveForeColor : ForeColor;
            return Enabled ? fore : UiPalette.TextSecondary;
        }

        private Color AnimatedFace()
        {
            if (!AnimateColors || !AnimationRunning || transitionTo.IsEmpty) return ResolveFace();
            float progress = Math.Min(1F, (float)(DateTime.UtcNow - transitionStarted).TotalMilliseconds / 120F);
            return UiPaint.Blend(transitionFrom, transitionTo, Math.Max(0F, progress));
        }

        private void StartTransition()
        {
            if (!AnimateColors) return;
            Color current = AnimatedFace();
            transitionFrom = current.A == 0 ? BackColor : current;
        }

        private void FinishTransition()
        {
            if (AnimateColors)
            {
                Color next = ResolveFace();
                transitionTo = next.A == 0 ? BackColor : next;
                transitionStarted = DateTime.UtcNow;
                ButtonAnimationClock.Start(this);
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && AnimateColors) ButtonAnimationClock.Remove(this);
            base.Dispose(disposing);
        }
    }

    internal sealed class SegmentedPicker : Control
    {
        private static readonly Color ContainerColor = UiPalette.CardElevated;
        // Accent 配白字只有 3.81:1；选中块用同色系深紫，保证小字达到 4.5:1。
        private static readonly Color SelectedColor = UiPalette.AccentPressed;
        private static readonly Color HoveredColor = UiPalette.ToolbarHover;
        private static readonly Color SelectedTextColor = Color.FromArgb(255, 255, 255);
        private static readonly Color TextColor = UiPalette.TextSecondary;
        private static readonly Color DisabledColor = UiPalette.ToolbarActive;

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

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

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

                Color text = !Enabled ? UiPalette.TextSecondary : selected ? SelectedTextColor : TextColor;
                TextRenderer.DrawText(graphics, items[i], Font, Rectangle.Round(cell), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (Focused && ShowFocusCues) UiPaint.DrawFocusRing(graphics, ClientRectangle);
        }
    }

    internal enum StatusDotState { Ready, Loading, Error }

    internal sealed class StatusDot : Control
    {
        private readonly ToolTip tooltip = new ToolTip();
        private StatusDotState state;

        internal StatusDot()
        {
            Size = new Size(7, 7);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleRole = AccessibleRole.StaticText;
            UpdateAccessibility();
        }

        internal StatusDotState State
        {
            get { return state; }
            set { state = value; UpdateAccessibility(); Invalidate(); }
        }

        private void UpdateAccessibility()
        {
            string text = state == StatusDotState.Loading ? "页面状态：加载中"
                : state == StatusDotState.Error ? "页面状态：出错" : "页面状态：正常";
            AccessibleName = text;
            tooltip.SetToolTip(this, text);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color color = state == StatusDotState.Loading ? Color.FromArgb(245, 158, 11)
                : state == StatusDotState.Error ? Color.FromArgb(239, 68, 68)
                : Color.FromArgb(34, 197, 94);
            e.Graphics.Clear(BackColor);
            using (SolidBrush brush = new SolidBrush(color)) e.Graphics.FillEllipse(brush, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tooltip != null) tooltip.Dispose();
            base.Dispose(disposing);
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

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color track;
            if (!Enabled) track = Checked ? UiPalette.ToolbarActive : UiPalette.Separator;
            else if (Checked) track = pressed ? UiPalette.AccentPressed : (hovered ? UiPalette.AccentHover : UiPalette.Accent);
            else track = pressed ? UiPalette.InkPressed : (hovered ? UiPalette.InkHover : UiPalette.Ink);

            // 焦点环画在轨道外侧的卡片底色上，避免紫色环落在开启状态的紫色轨道里看不见。
            bool focus = Focused && ShowFocusCues;
            float trackInset = focus ? 4F : 0.5F;
            RectangleF trackRect = new RectangleF(trackInset, trackInset, Width - trackInset * 2F, Height - trackInset * 2F);
            using (GraphicsPath path = UiPaint.CapsulePath(trackRect))
            using (SolidBrush brush = new SolidBrush(track))
                graphics.FillPath(brush, path);

            float inset = Math.Max(2F, Height * 0.077F) + (focus ? 3F : 0F);
            float knob = Height - inset * 2F;
            float knobX = Checked ? Width - knob - inset : inset;
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(52, 0, 0, 0)))
                graphics.FillEllipse(shadow, knobX + 0.7F, inset + 1.3F, knob, knob);
            using (SolidBrush face = new SolidBrush(Enabled ? Color.White : Color.FromArgb(216, 216, 216)))
                graphics.FillEllipse(face, knobX, inset, knob, knob);
            if (Focused && ShowFocusCues) UiPaint.DrawFocusRing(graphics, ClientRectangle);
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
            BackColor = UiPalette.Page;
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
            using (SolidBrush trackBrush = new SolidBrush(UiPalette.Page))
                e.Graphics.FillRectangle(trackBrush, track);

            Rectangle thumb = new Rectangle(Width - ScrollBarWidth + 1, ThumbY, ScrollBarWidth - 2, ThumbHeight);
            Color thumbColor = dragging ? UiPalette.Accent
                : hoverThumb ? UiPalette.TextSecondary : UiPalette.TextTertiary;
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
