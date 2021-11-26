#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Joko.NINA.Plugins.TenMicron.Utility {

    internal static class OSInterop {

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int smIndex);

        public const int SM_CMONITORS = 80;

        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(int nAction, int nParam, ref RECT rc, int nUpdate);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(HandleRef hmonitor, [In, Out] MONITORINFOEX info);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(HandleRef handle, int flags);

        public struct RECT {
            public int left;
            public int top;
            public int right;
            public int bottom;
            public int width { get { return right - left; } }
            public int height { get { return bottom - top; } }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Auto)]
        public class MONITORINFOEX {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public char[] szDevice = new char[32];

            public int dwFlags;
        }
    }

    internal static class WPFExtensionMethods {

        private static double GetScalingFactor(Window w) {
            Matrix m = PresentationSource.FromVisual(Application.Current.MainWindow).CompositionTarget.TransformToDevice;
            return m.M11;
        }

        public static Rect GetAbsolutePosition(this Window w) {
            if (w.WindowState != WindowState.Maximized)
                return new Rect(w.Left, w.Top, w.Width, w.Height);

            Rect r;
            bool multimonSupported = OSInterop.GetSystemMetrics(OSInterop.SM_CMONITORS) != 0;
            var scalingFactor = GetScalingFactor(w);
            if (!multimonSupported) {
                OSInterop.RECT rc = new OSInterop.RECT();
                OSInterop.SystemParametersInfo(48, 0, ref rc, 0);
                r = new Rect(rc.left / scalingFactor, rc.top / scalingFactor, rc.width / scalingFactor, rc.height / scalingFactor);
            } else {
                WindowInteropHelper helper = new WindowInteropHelper(w);
                IntPtr hmonitor = OSInterop.MonitorFromWindow(new HandleRef((object)null, helper.EnsureHandle()), 2);
                OSInterop.MONITORINFOEX info = new OSInterop.MONITORINFOEX();
                OSInterop.GetMonitorInfo(new HandleRef((object)null, hmonitor), info);
                r = new Rect(info.rcWork.left / scalingFactor, info.rcWork.top / scalingFactor, info.rcWork.width / scalingFactor, info.rcWork.height / scalingFactor);
            }
            return r;
        }
    }
}