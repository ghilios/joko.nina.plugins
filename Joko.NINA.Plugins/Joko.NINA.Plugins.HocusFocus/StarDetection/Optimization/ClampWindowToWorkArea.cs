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
using System.Windows.Interop;

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

        // One hook per window; the modal dialog and its HwndSource are short-lived, so the hook dies with the window.
        // The table only guards against a repeated Loaded re-adding the hook, and lets the window be collected.
        private static readonly ConditionalWeakTable<Window, object> hookedWindows = new();

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
            }
            // The first shown step is small, but clamp the current bounds defensively in case it already overshoots.
            ClampNow(hwnd);
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

        /// <summary>One-time clamp of the window's current bounds into the work area (height only; keeps it on-screen).</summary>
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
