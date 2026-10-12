using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniView.WebView2App
{
    // 贴边自动隐藏：把窗口拖到屏幕左/右边缘就吸附收起，鼠标回到原来那段边缘滑回来，
    // 鼠标再移开又收起。与「鼠标移出自动隐藏」互斥（见 WindowRules.EffectiveMouseLeaveAutoHide）：
    // 那套逻辑靠"鼠标离开窗口"触发后是真正的老板键式隐藏，同时生效会抢先隐藏窗口，
    // 贴边收起永远不会发生。
    internal sealed partial class MainForm
    {
        private EdgeDockSide edgeDockSide = EdgeDockSide.None;
        // 收起后应当停靠的位置（屏幕坐标）；隐藏期间 Bounds 不变，但触发段用它做唯一依据，
        // 免得窗口在隐藏期间被其它逻辑挪动后触发段跟着漂移。
        private Rectangle edgeDockAnchor = Rectangle.Empty;
        // 刚收起时光标还停在触发段上，必须先离开再回来才允许唤出，否则拖到边缘的瞬间就会弹回。
        private bool edgeDockRevealArmed;
        private DateTime? edgeDockLeaveSince;
        // 一次拖动只记一条吸附日志，否则 WM_MOVING 每来一次都会刷屏。
        private bool edgeSnapReported;

        private Rectangle EdgeDockRevealZone
        {
            get
            {
                if (edgeDockSide == EdgeDockSide.None || edgeDockAnchor.IsEmpty) return Rectangle.Empty;
                return WindowRules.CalculateEdgeRevealZone(edgeDockAnchor,
                    Screen.FromRectangle(edgeDockAnchor).WorkingArea, edgeDockSide,
                    Scaled(WindowRules.EdgeDockBand), Scaled(WindowRules.EdgeDockSpanPadding));
            }
        }

        /// <summary>
        /// WM_MOVING：拖动过程中把左右两边吸到屏幕边缘，并挡住继续往屏幕外的方向。
        /// 用这条消息而不是自己接管拖动，是因为系统把"提案位置矩形"直接交给了程序改——这正是
        /// 留给移动时约束/吸附的口子，不必和原生移动循环抢控制权，也不会抖。lParam 是 RECT 指针。
        /// </summary>
        private void ApplyMovingSnap(ref Message m)
        {
            if (m.LParam == IntPtr.Zero) return;
            NativeMethods.NativeRectangle rect =
                (NativeMethods.NativeRectangle)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.NativeRectangle));
            Rectangle proposed = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            // 用"提案矩形所在的屏幕"而不是当前 Bounds 所在的屏幕：多显示器下后者会把窗口一直挡在
            // 原来那台显示器上——那台显示器的边缘在桌面中间，并不是真正的桌面边缘。
            Rectangle workArea = Screen.FromRectangle(proposed).WorkingArea;
            Rectangle snapped = WindowRules.SnapSideEdges(proposed, Bounds, workArea,
                Scaled(WindowRules.EdgeDockThreshold));
            if (snapped == proposed) return;
            rect.Left = snapped.Left;
            rect.Top = snapped.Top;
            rect.Right = snapped.Right;
            rect.Bottom = snapped.Bottom;
            Marshal.StructureToPtr(rect, m.LParam, false);
            if (edgeSnapReported) return;
            edgeSnapReported = true;
            Diagnostics.Log("edge-snap proposed=" + proposed + " snapped=" + snapped);
        }

        /// <summary>拖动/缩放结束后调用：贴到左右边缘就吸附收起，否则解除贴边。</summary>
        private void EvaluateEdgeDock()
        {
            if (isQuitting || IsDisposed || !IsHandleCreated) return;
            if (!settings.EdgeAutoHideEnabled || miniMode)
            {
                UndockEdge();
                return;
            }
            Rectangle workArea = Screen.FromRectangle(Bounds).WorkingArea;
            EdgeDockSide side = WindowRules.ResolveEdgeDockSide(Bounds, workArea, Scaled(WindowRules.EdgeDockThreshold));
            Diagnostics.Log("edge-dock evaluate side=" + side + " bounds=" + Bounds);
            if (side == EdgeDockSide.None)
            {
                UndockEdge();
                return;
            }
            DockEdge(side, workArea);
        }

        private void DockEdge(EdgeDockSide side, Rectangle workArea)
        {
            bool sideChanged = side != edgeDockSide;
            edgeDockSide = side;
            edgeDockAnchor = WindowRules.CalculateEdgeDockBounds(workArea, Bounds, side);
            if (Bounds != edgeDockAnchor) SetBoundsProgrammatically(edgeDockAnchor);
            edgeDockRevealArmed = false;
            edgeDockLeaveSince = null;
            Diagnostics.Log("edge-dock side=" + side + " anchor=" + edgeDockAnchor);
            if (sideChanged) UpdateSettingsUi();
            DockHide();
        }

        private void DockHide()
        {
            if (!Visible || !settings.EdgeAutoHideEnabled || edgeDockSide == EdgeDockSide.None) return;
            // 恢复手段都不可用时保持可见：贴边收起本身也能靠碰边唤回，但多一道保险，
            // 与「鼠标移出自动隐藏」的 CanRestoreWindow 要求保持一致。
            if (!CanRestoreWindow) return;
            HideWindowCore();
            // 收起后 100 Hz 轮询没必要，但也不能用常规隐藏的 250ms——碰边唤出要跟手。
            pointerTimer.Interval = EdgeDockPollIntervalMilliseconds;
        }

        private void RevealDockedWindow()
        {
            if (edgeDockSide == EdgeDockSide.None || Visible) return;
            Diagnostics.Log("edge-dock reveal side=" + edgeDockSide);
            edgeDockLeaveSince = null;
            if (Bounds != edgeDockAnchor) SetBoundsProgrammatically(edgeDockAnchor);
            ShowWindowCore();
        }

        /// <summary>解除贴边。窗口位置和可见性不动，由调用方决定接下来怎么显示。</summary>
        private void UndockEdge()
        {
            if (edgeDockSide == EdgeDockSide.None && !edgeDockRevealArmed && !edgeDockLeaveSince.HasValue) return;
            Diagnostics.Log("edge-dock undock side=" + edgeDockSide);
            edgeDockSide = EdgeDockSide.None;
            edgeDockAnchor = Rectangle.Empty;
            edgeDockRevealArmed = false;
            edgeDockLeaveSince = null;
        }

        /// <summary>
        /// 窗口被其它逻辑改过尺寸或位置（直播横屏切换）后，把贴边位置和触发段跟着更新。
        /// 保持贴边状态：这两条路径是自动触发的，不是用户按了隐藏/恢复，
        /// 若在此解除贴边，处于收起状态的窗口就会失去碰边唤回这条路。
        /// </summary>
        private void RefreshEdgeDockAnchor()
        {
            if (edgeDockSide == EdgeDockSide.None || IsDisposed || !IsHandleCreated) return;
            Rectangle workArea = Screen.FromRectangle(Bounds).WorkingArea;
            edgeDockAnchor = WindowRules.CalculateEdgeDockBounds(workArea, Bounds, edgeDockSide);
            if (Bounds != edgeDockAnchor) SetBoundsProgrammatically(edgeDockAnchor);
            Diagnostics.Log("edge-dock anchor refresh " + edgeDockAnchor);
        }

        /// <summary>贴边状态下的鼠标轮询：收起时等光标贴住那一段边缘唤出，唤出后鼠标离开再收起。</summary>
        private void MonitorEdgeDock()
        {
            if (IsTestMode || domProbe || isQuitting || IsDisposed || !IsHandleCreated) return;
            if (!settings.EdgeAutoHideEnabled || edgeDockSide == EdgeDockSide.None) return;
            Rectangle zone = EdgeDockRevealZone;
            if (zone.IsEmpty) return;
            Point cursor = Cursor.Position;

            if (!Visible)
            {
                if (!edgeDockRevealArmed)
                {
                    if (!zone.Contains(cursor)) edgeDockRevealArmed = true;
                    return;
                }
                if (WindowRules.ShouldRevealDockedWindow(cursor, zone, edgeDockRevealArmed)) RevealDockedWindow();
                return;
            }

            // 已唤出：鼠标离开窗口和触发段并达到延迟后再次收起。拖动期间不收起，
            // 设置页打开时也不打扰——用户正在配置这个开关。
            if (settingsOpen || Control.MouseButtons != MouseButtons.None)
            {
                edgeDockLeaveSince = null;
                return;
            }
            Rectangle window = Bounds;
            int margin = Scaled(AutoHidePointerMargin);
            window.Inflate(margin, margin);
            if (window.Contains(cursor) || zone.Contains(cursor))
            {
                edgeDockLeaveSince = null;
                return;
            }
            if (!edgeDockLeaveSince.HasValue) edgeDockLeaveSince = DateTime.UtcNow.AddMilliseconds(-pointerTimer.Interval);
            if ((DateTime.UtcNow - edgeDockLeaveSince.Value).TotalMilliseconds < WindowRules.EdgeDockLeaveDelayMilliseconds) return;
            edgeDockLeaveSince = null;
            DockHide();
        }

        /// <summary>设置页开关。开启时接管「鼠标移出自动隐藏」，关闭时把先前的选择还回去。</summary>
        private void SetEdgeAutoHide(bool enabled)
        {
            if (enabled == settings.EdgeAutoHideEnabled) return;
            if (enabled)
            {
                settings.AutoHideEnabledBeforeEdge = settings.AutoHideEnabled;
                settings.AutoHideEnabled = false;
                outsideSince = null;
            }
            else
            {
                settings.AutoHideEnabled = settings.AutoHideEnabledBeforeEdge;
                // 正处于贴边收起状态时关掉开关，得先把窗口放回来，否则它会一直藏着。
                if (!Visible) ShowWindowCore();
                UndockEdge();
            }
            settings.EdgeAutoHideEnabled = enabled;
            Diagnostics.Log("edge-auto-hide=" + enabled + " mouse-leave-auto-hide=" + settings.AutoHideEnabled);
            ScheduleSettingsSave();
            UpdateSettingsUi();
        }
    }
}
