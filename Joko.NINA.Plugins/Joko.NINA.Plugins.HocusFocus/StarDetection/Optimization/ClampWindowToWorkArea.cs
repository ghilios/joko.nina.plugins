#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NINA.Core.Utility;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Attached behavior that keeps the host <see cref="Window"/> within the working area (screen minus the taskbar)
    /// of the monitor it is on. NINA hosts the wizard in a custom-chrome window sized to its content, which on a small
    /// laptop opens taller than the screen and (being custom-chrome) maximizes over the taskbar, so the footer buttons
    /// end up off-screen or behind the taskbar.
    ///
    /// A WPF <c>MaxHeight</c> is not reliable here (custom chrome + per-monitor DPI), so this works at the Win32 level in
    /// physical pixels, which is DPI-agnostic:
    /// <list type="bullet">
    /// <item>WM_GETMINMAXINFO — maximize fills the work area (taskbar stays visible) and the max track size is capped.</item>
    /// <item>WM_WINDOWPOSCHANGING — every normal-state resize (including NINA's per-step SizeToContent growth and user
    /// drag) is clamped to the work-area height and nudged back on-screen.</item>
    /// </list>
    /// Paired with the wizard body's ScrollViewer, content taller than the work area then scrolls while the footer stays
    /// pinned. Applied as <c>local:ClampWindowToWorkArea.Enabled="True"</c> on the wizard DataTemplate's root element.
    /// </summary>
    public static class ClampWindowToWorkArea {

        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(ClampWindowToWorkArea),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        /// <summary>
        /// Opt in to GROWING the window so the body's ScrollViewer shows everything, up to the work area — the
        /// wizard's behaviour (F76). OFF by default, and deliberately so: <see cref="EnabledProperty"/> alone gives
        /// the work-area clamp, which is what a window wants when it must never open or be dragged off-screen.
        ///
        /// The review windows must NOT set this. They host an image viewport whose ScrollViewer extent is the
        /// IMAGE, so growing to it would size the window to the picture rather than to a sensible dialog, and would
        /// fight <c>ReviewViewportHostBase</c>'s own fit-to-viewport logic.
        /// </summary>
        public static readonly DependencyProperty FitContentProperty =
            DependencyProperty.RegisterAttached(
                "FitContent",
                typeof(bool),
                typeof(ClampWindowToWorkArea),
                new PropertyMetadata(false));

        public static bool GetFitContent(DependencyObject obj) => (bool)obj.GetValue(FitContentProperty);

        public static void SetFitContent(DependencyObject obj, bool value) => obj.SetValue(FitContentProperty, value);

        // Dedupe the F76 diagnostic: SizeChanged fires often and only distinct states are informative.
        private static string lastLoggedNote;

        /// <summary>Logs a line at most once per distinct message. Every EXIT from the fit path goes through this:
        /// a check that declines without saying why is what made this defect look like "no change" (F66).</summary>
        private static void LogOnce(string note) {
            if (note != lastLoggedNote) {
                lastLoggedNote = note;
                Logger.Info(note);
            }
        }

        // One hook per window; the modal dialog and its HwndSource are short-lived, so the hook dies with the window.
        // The table only guards against a repeated Loaded re-adding the hook, and lets the window be collected.
        private static readonly ConditionalWeakTable<Window, object> hookedWindows = new();

        // The element the attached property lives on (the wizard DataTemplate's root Grid) IS the content to
        // measure. Window.Content is not usable here: on NINA's custom-chrome CustomWindow it is not the template
        // root, so `window.Content is FrameworkElement` failed and TryFitToContent declined on every call -- while
        // logging nothing, so the log showed no `F76 fit:` line at all and the rule looked like it had no effect.
        private static readonly ConditionalWeakTable<Window, FrameworkElement> contentRoots = new();

        // One queued resize per window. SizeChanged fires repeatedly during a resize and each would otherwise
        // queue its own dispatcher callback.
        private static readonly ConditionalWeakTable<Window, Window> pendingFits = new();

        // Windows that opted in to growing to their content. Clamp-only is the default.
        private static readonly ConditionalWeakTable<Window, Window> fitContentWindows = new();

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not FrameworkElement fe) {
                return;
            }
            if ((bool)e.NewValue) {
                fe.Loaded += OnLoaded;
                if (fe.IsLoaded) {
                    Attach(fe);
                }
            } else {
                fe.Loaded -= OnLoaded;
                if (Window.GetWindow(fe) is Window w) {
                    w.SizeChanged -= OnWindowSizeChanged;
                }
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe) {
                Attach(fe);
            }
        }

        private static void Attach(FrameworkElement fe) {
            var window = Window.GetWindow(fe);
            if (window is null) {
                return;
            }
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) {
                return;
            }
            var source = HwndSource.FromHwnd(hwnd);
            if (source is null) {
                return;
            }
            if (!hookedWindows.TryGetValue(window, out _)) {
                hookedWindows.Add(window, hookedWindows);
                HwndSourceHook hook = (IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    WndProc(window, h, msg, lParam, ref handled);
                source.AddHook(hook);
                // F76. The Win32 hook alone is NOT enough. It edits pos.cy inside individual messages while
                // SizeToContent stays ACTIVE, so WPF recomputes the content height on the next layout pass and
                // re-asserts it — and WPF wins. On a screen small enough that the wizard's summary greatly
                // exceeds the work area the window therefore ends up taller than the screen, and the footer
                // (Back / Review frames / Continue optimizing / Accept / Close) is off the bottom. Dragging it
                // only snaps it to work.Top via the y-clamp below, with the bottom still off-screen; the reason a
                // manual resize "fixes" it is that WPF sets SizeToContent = Manual automatically when the USER
                // resizes. So do that ourselves, in WPF, the way Review/ReviewViewportHostBase already does.
                window.SizeChanged += OnWindowSizeChanged;
                contentRoots.Remove(window);
                contentRoots.Add(window, fe);
                if (GetFitContent(fe)) {
                    fitContentWindows.Remove(window);
                    fitContentWindows.Add(window, window);
                }
                Logger.Info($"F76 clamp: attached to '{window.GetType().Name}' hwnd={hwnd}; workArea={SystemParameters.WorkArea}");
            }
            // The first shown step is small, but clamp the current bounds defensively in case it already overshoots.
            ApplyWorkAreaLimit(window, SystemParameters.WorkArea);
            ClampNow(hwnd);
        }

        private static void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is Window w) {
                ApplyWorkAreaLimit(w, SystemParameters.WorkArea);
            }
        }

        /// <summary>
        /// Stops WPF re-growing the window past the work area, in device-independent pixels.
        ///
        /// Engages ONLY once the window would exceed the work area, so ordinary per-step SizeToContent growth is
        /// untouched on a screen with room for it. Once it engages it turns SizeToContent OFF — that is the whole
        /// point (F76): leaving it on is what lets WPF overwrite the Win32 clamp on the next layout pass.
        ///
        /// Re-entrant by construction: setting Height raises SizeChanged again, and the second pass sees
        /// height == work.Height and returns false.
        /// </summary>
        /// <returns>true if the window was clamped.</returns>
        internal static bool ApplyWorkAreaLimit(Window window, Rect work) {
            if (window is null || work.Height <= 0.0) {
                return false;
            }
            if (fitContentWindows.TryGetValue(window, out _) && TryFitToContent(window, work)) {
                return true;
            }
            var height = EffectiveHeight(window.ActualHeight, window.Height);
            var clamping = TryComputeWorkAreaClamp(height, window.Top, work, out var newHeight, out var newTop);
            // F76 has been misdiagnosed three times from reasoning without instrumentation, and a fix shipped that
            // did not work. Make the behaviour self-reporting: one line per DISTINCT state, so the log says whether
            // the clamp engaged and on what numbers, instead of the next person inferring it.
            var note = $"F76 clamp: h={height:F0} actual={window.ActualHeight:F0} top={window.Top:F0} " +
                       $"stc={window.SizeToContent} work={work.Height:F0}@{work.Top:F0} => " +
                       (clamping ? $"CLAMP h={newHeight:F0} top={newTop:F0}" : "declined");
            if (note != lastLoggedNote) {
                lastLoggedNote = note;
                Logger.Info(note);
            }
            if (!clamping) {
                return false;
            }
            if (newHeight < height) {
                // Stop WPF re-asserting the content HEIGHT over the clamp -- but only the height. Setting
                // SizeToContent.Manual froze the WIDTH too, at whatever an earlier and narrower wizard step needed,
                // and the summary footer (Back / Review frames / Continue optimizing / Accept / Close) then no longer
                // fit horizontally: Accept and Close were clipped INSIDE the window. That was a regression this fix
                // introduced, reported from the field. Drop only the Height flag and leave width auto-sizing on.
                window.SizeToContent = window.SizeToContent == SizeToContent.WidthAndHeight
                    ? SizeToContent.Width
                    : SizeToContent.Manual;
                window.Height = newHeight;
            }
            window.Top = newTop;
            return true;
        }

        /// <summary>
        /// The height the window SHOULD have: everything the content wants to render, capped at the work area.
        ///
        /// Asked for directly by the owner — with the footer finally on screen, the window was settling ~400 px
        /// SHORTER than the screen while still showing a scrollbar, so content was being scrolled that there was
        /// room to display. The cause is that a <c>ScrollViewer</c> has no natural desired height: it reports
        /// whatever height it is offered, so <c>SizeToContent</c> converges on an arbitrary smaller window instead
        /// of on the content's real height. Measuring the content against an INFINITE height is what recovers the
        /// number WPF cannot supply here.
        /// </summary>
        internal static double ChooseWindowHeight(double contentDesiredHeight, double chromeHeight, double workAreaHeight) {
            if (workAreaHeight <= 0.0) {
                return double.NaN;
            }
            if (double.IsNaN(contentDesiredHeight) || contentDesiredHeight <= 0.0) {
                return double.NaN;
            }
            var chrome = double.IsNaN(chromeHeight) || chromeHeight < 0.0 ? 0.0 : chromeHeight;
            return Math.Min(contentDesiredHeight + chrome, workAreaHeight);
        }

        /// <summary>
        /// The height the window should have, derived from how much the body is ACTUALLY scrolling.
        ///
        /// Measuring the content was the wrong instrument twice over. SizeToContent asks a ScrollViewer for a
        /// desired height and it reports whatever it is offered; and measuring the template root against an
        /// infinite height does not help either, because the ScrollViewer sits in a <c>Height="*"</c> grid row,
        /// which collapses to its desired size rather than its content's. Measured in the field: it answered
        /// <c>content=548</c> for a body that genuinely overflowed a 752 work area.
        ///
        /// The ScrollViewer already knows the number. <c>ExtentHeight - ViewportHeight</c> is exactly how much
        /// content is off-screen, so growing the window by that much shows it — capped at the work area. Naturally
        /// idempotent: once nothing is clipped the shortfall is 0 and the target equals the current height.
        /// </summary>
        internal static double ChooseWindowHeightFromOverflow(double currentHeight, double extentHeight, double viewportHeight, double workAreaHeight) {
            if (workAreaHeight <= 0.0 || currentHeight <= 0.0) {
                return double.NaN;
            }
            if (double.IsNaN(extentHeight) || double.IsNaN(viewportHeight) || viewportHeight <= 0.0) {
                return double.NaN;
            }
            var shortfall = Math.Max(0.0, extentHeight - viewportHeight);
            return Math.Min(currentHeight + shortfall, workAreaHeight);
        }

        /// <summary>Depth-first search for the first ScrollViewer under <paramref name="root"/>.</summary>
        private static ScrollViewer FindScrollViewer(DependencyObject root) {
            if (root is null) {
                return null;
            }
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) {
                    return sv;
                }
                var found = FindScrollViewer(child);
                if (found != null) {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Measures what the window's content wants vertically, given unlimited height, and fits the window to
        /// <see cref="ChooseWindowHeight"/>. Returns false when the content cannot be measured, so the caller falls
        /// back to the plain overflow clamp rather than guessing.
        /// </summary>
        private static bool TryFitToContent(Window window, Rect work) {
            if (!contentRoots.TryGetValue(window, out var root) || root is null) {
                LogOnce("F76 fit: DECLINED -- no content root recorded for this window");
                return false;
            }
            if (root.ActualWidth <= 0.0) {
                LogOnce($"F76 fit: DECLINED -- content root not laid out yet (ActualWidth={root.ActualWidth:F0})");
                return false;
            }
            var current = EffectiveHeight(window.ActualHeight, window.Height);
            var scroller = FindScrollViewer(root);
            if (scroller is null) {
                LogOnce("F76 fit: DECLINED -- no ScrollViewer under the content root");
                return false;
            }
            var target = ChooseWindowHeightFromOverflow(current, scroller.ExtentHeight, scroller.ViewportHeight, work.Height);
            if (double.IsNaN(target)) {
                LogOnce($"F76 fit: DECLINED -- unusable scroll metrics (extent={scroller.ExtentHeight:F0} " +
                        $"viewport={scroller.ViewportHeight:F0} current={current:F0} work={work.Height:F0})");
                return false;
            }
            var top = window.Top;
            if (top + target > work.Bottom) {
                top = work.Bottom - target;
            }
            if (top < work.Top) {
                top = work.Top;
            }
            LogOnce($"F76 fit: extent={scroller.ExtentHeight:F0} viewport={scroller.ViewportHeight:F0} " +
                    $"short={Math.Max(0.0, scroller.ExtentHeight - scroller.ViewportHeight):F0} work={work.Height:F0} " +
                    $"current={current:F0}@{window.Top:F0} => h={target:F0} top={top:F0}");
            if (Math.Abs(current - target) < 1.0 && Math.Abs(window.Top - top) < 1.0) {
                return true;   // already right; do not churn layout
            }
            if (window.SizeToContent == SizeToContent.WidthAndHeight) {
                window.SizeToContent = SizeToContent.Width;   // keep width auto (F76: Manual clipped Accept/Close)
            } else if (window.SizeToContent == SizeToContent.Height) {
                window.SizeToContent = SizeToContent.Manual;
            }
            // APPLY OUTSIDE THE LAYOUT PASS. This runs from SizeChanged, and a window's size assigned from inside
            // its own SizeChanged is made during layout and is discarded. Proven in the field rather than assumed:
            // the read-back showed `target=752 -> Height=492 ... (was H=492)` with max=Infinity, i.e. nothing was
            // capping it -- the write simply did not stick. Posting to the dispatcher runs it after layout settles.
            if (pendingFits.TryGetValue(window, out _)) {
                return true;   // one queued application is enough; SizeChanged fires many times per resize
            }
            pendingFits.Add(window, window);
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                pendingFits.Remove(window);
                // GO FULLY MANUAL FIRST, KEEPING THE WIDTH AUTO-SIZING ALREADY PRODUCED.
                //
                // Measured: SetWindowPos honoured the MOVE and ignored the RESIZE in the same call --
                //   "asked cy=1128px y=0px; rect was 738px@119 now 738px@0; wpf H=492 A=492 stc=Width"
                // and 738px / 1.5 = 492 DIPs, exactly WPF's own Height. With SizeToContent still set (to Width),
                // WPF re-applies its size on every layout pass and overwrites both the property assignment and the
                // native resize. Clearing it is what lets a height stick.
                //
                // The width is explicitly carried over rather than left to Manual's default. Freezing the width at
                // whatever an earlier, narrower step used is what clipped Accept and Close before; by the summary
                // step SizeToContent.Width has already produced the correct width, so pinning THAT keeps the footer
                // intact while handing us the height.
                if (window.SizeToContent != SizeToContent.Manual) {
                    var keepWidth = window.ActualWidth;
                    window.SizeToContent = SizeToContent.Manual;
                    if (keepWidth > 0.0) {
                        window.Width = keepWidth;
                    }
                }
                window.Height = target;
                window.Top = top;

                // SET IT THROUGH WIN32 TOO, as a belt-and-braces follow-up. Measured twice in the field: assigning
                // window.Height = 752 reads back as the OLD value immediately, both inside SizeChanged and from a
                // dispatcher callback, with MaxHeight = Infinity so nothing was capping it. WPF is refusing the
                // write. SetWindowPos is not refused -- ClampNow in this same file already repositions this very
                // window that way, which is how it lands at top=0.
                var helper = new WindowInteropHelper(window);
                var hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var before)) {
                    LogOnce("F76 applied: DECLINED -- no hwnd to resize");
                    return;
                }
                // The work area and SetWindowPos are in PHYSICAL pixels; target/top are DIPs. Convert with the
                // window's own composition target rather than assuming a scale factor.
                var toDevice = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice
                               ?? Matrix.Identity;
                var cy = (int)Math.Round(target * toDevice.M22);
                var y = (int)Math.Round(top * toDevice.M22);
                var cx = before.Right - before.Left;
                SetWindowPos(hwnd, IntPtr.Zero, before.Left, y, cx, cy, SWP_NOZORDER | SWP_NOACTIVATE);
                GetWindowRect(hwnd, out var after);
                LogOnce($"F76 applied: target={target:F0}dip -> asked cy={cy}px y={y}px; " +
                        $"rect was {before.Bottom - before.Top}px@{before.Top} now {after.Bottom - after.Top}px@{after.Top}; " +
                        $"wpf H={window.Height:F0} A={window.ActualHeight:F0} W={window.Width:F0} " +
                        $"scale={toDevice.M22:F2} stc={window.SizeToContent}");
            }));
            return true;
        }

        /// <summary>
        /// Which height decides whether the window overflows. MEASURED, not assumed: on the failing window the log
        /// recorded <c>h=492 actual=817 top=123 work=752</c> — <see cref="FrameworkElement.ActualHeight"/> is the
        /// RENDERED height and had already overflowed, while <see cref="FrameworkElement.Height"/> is the REQUESTED
        /// value and lagged at 492. Reading Height alone is why the shipped clamp declined on every call and the
        /// footer stayed off-screen. Take whichever is larger: the rendered height is what puts Accept off the
        /// bottom, and a larger pending request would do so next layout pass.
        /// </summary>
        internal static double EffectiveHeight(double actualHeight, double heightProperty) {
            var requested = double.IsNaN(heightProperty) ? 0.0 : heightProperty;
            return Math.Max(actualHeight, requested);
        }

        /// <summary>
        /// The geometry decision, split out so it is testable WITHOUT constructing a WPF <see cref="Window"/> —
        /// a Window-constructing STA fixture hangs the CI testhost (see WorkAreaClampGeometryTests).
        /// </summary>
        internal static bool TryComputeWorkAreaClamp(double height, double top, Rect work, out double newHeight, out double newTop) {
            newHeight = height;
            newTop = top;
            if (work.Height <= 0.0) {
                return false;
            }
            // The question is "does the window FIT INSIDE the work area", not "is it too tall". The first version
            // asked only the second, and that is why the shipped fix did nothing: the Win32 hook caps the height
            // FIRST, so by the time this runs height already equals work.Height and the too-tall test declines --
            // leaving the top exactly where it was. The measured failing rect was T=444 B=1836 h=1392 against a
            // work area of 0..1392: the height was already correct and the POSITION was the whole defect.
            var h = Math.Min(height, work.Height);
            var t = top;
            if (t + h > work.Bottom) {
                t = work.Bottom - h;
            }
            if (t < work.Top) {
                t = work.Top;
            }
            if (h == height && t == top) {
                return false;
            }
            newHeight = h;
            newTop = t;
            return true;
        }

        private static IntPtr WndProc(Window window, IntPtr hwnd, int msg, IntPtr lParam, ref bool handled) {
            switch (msg) {
                case WM_GETMINMAXINFO:
                    if (TryGetWorkArea(hwnd, out var maxWork, out var monitor)) {
                        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                        // Maximize to the work area (taskbar stays visible) instead of the full monitor.
                        mmi.ptMaxPosition.X = maxWork.Left - monitor.Left;
                        mmi.ptMaxPosition.Y = maxWork.Top - monitor.Top;
                        mmi.ptMaxSize.X = maxWork.Right - maxWork.Left;
                        mmi.ptMaxSize.Y = maxWork.Bottom - maxWork.Top;
                        // Cap how large the window can be made (covers SizeToContent and user drag).
                        mmi.ptMaxTrackSize.X = maxWork.Right - maxWork.Left;
                        mmi.ptMaxTrackSize.Y = maxWork.Bottom - maxWork.Top;
                        Marshal.StructureToPtr(mmi, lParam, false);
                        handled = true; // take over so WPF's default (unbounded) max does not overwrite ours
                    }
                    break;

                case WM_WINDOWPOSCHANGING:
                    if (window.WindowState == WindowState.Normal && TryGetWorkArea(hwnd, out var work, out _)) {
                        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                        var sizing = (pos.flags & SWP_NOSIZE) == 0;
                        if (sizing) {
                            var changed = false;
                            var maxHeight = work.Bottom - work.Top;
                            if (pos.cy > maxHeight) {
                                pos.cy = maxHeight;
                                changed = true;
                            }
                            // If this change also moves the window, keep the (clamped) window inside the work area.
                            if ((pos.flags & SWP_NOMOVE) == 0) {
                                if (pos.y + pos.cy > work.Bottom) {
                                    pos.y = work.Bottom - pos.cy;
                                    changed = true;
                                }
                                if (pos.y < work.Top) {
                                    pos.y = work.Top;
                                    changed = true;
                                }
                            }
                            if (changed) {
                                Marshal.StructureToPtr(pos, lParam, false);
                            }
                        }
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        /// <summary>One-time clamp of the window's current bounds into the work area, in PHYSICAL pixels: height,
        /// and the top so the bottom stays inside. Complements <see cref="ApplyWorkAreaLimit"/>, which is the WPF-level
        /// half and is the one that stops SizeToContent re-growing the window (F76).</summary>
        private static void ClampNow(IntPtr hwnd) {
            if (!TryGetWorkArea(hwnd, out var work, out _) || !GetWindowRect(hwnd, out var r)) {
                return;
            }
            int cx = r.Right - r.Left, cy = r.Bottom - r.Top, x = r.Left, y = r.Top;
            int workHeight = work.Bottom - work.Top;
            var changed = false;
            if (cy > workHeight) {
                cy = workHeight;
                changed = true;
            }
            if (y < work.Top) {
                y = work.Top;
                changed = true;
            }
            if (y + cy > work.Bottom) {
                y = work.Bottom - cy;
                changed = true;
            }
            if (changed) {
                SetWindowPos(hwnd, IntPtr.Zero, x, y, cx, cy, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        private static bool TryGetWorkArea(IntPtr hwnd, out RECT work, out RECT monitor) {
            work = default;
            monitor = default;
            var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero) {
                return false;
            }
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref info)) {
                return false;
            }
            work = info.rcWork;
            monitor = info.rcMonitor;
            return true;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const int MONITOR_DEFAULTTONEAREST = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }
    }
}
