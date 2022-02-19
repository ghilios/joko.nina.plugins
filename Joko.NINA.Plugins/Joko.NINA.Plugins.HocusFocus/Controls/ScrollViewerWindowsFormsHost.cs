#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    // https://stackoverflow.com/questions/14080580/scrollviewer-is-not-working-in-wpf-windowsformhost
    public class ScrollViewerWindowsFormsHost : WindowsFormsHost {

        protected override void OnWindowPositionChanged(Rect rcBoundingBox) {
            base.OnWindowPositionChanged(rcBoundingBox);

            if (ParentScrollViewer == null)
                return;

            GeneralTransform tr = ParentScrollViewer.TransformToAncestor(MainWindow);
            var scrollRect = new Rect(new Size(ParentScrollViewer.ViewportWidth, ParentScrollViewer.ViewportHeight));
            scrollRect = tr.TransformBounds(scrollRect);

            var intersect = Rect.Intersect(scrollRect, rcBoundingBox);
            if (!intersect.IsEmpty) {
                tr = MainWindow.TransformToDescendant(this);
                intersect = tr.TransformBounds(intersect);
            }

            SetRegion(intersect);
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent) {
            base.OnVisualParentChanged(oldParent);
            ParentScrollViewer = null;

            var p = Parent as FrameworkElement;
            while (p != null) {
                if (p is ScrollViewer) {
                    ParentScrollViewer = (ScrollViewer)p;
                    break;
                }

                p = p.Parent as FrameworkElement;
            }
        }

        private void SetRegion(Rect intersect) {
            using (var graphics = System.Drawing.Graphics.FromHwnd(Handle))
                SetWindowRgn(Handle, (new System.Drawing.Region(ConvertRect(intersect))).GetHrgn(graphics), true);
        }

        private static System.Drawing.RectangleF ConvertRect(Rect r) {
            return new System.Drawing.RectangleF((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
        }

        private Window _mainWindow;

        private Window MainWindow {
            get {
                if (_mainWindow == null)
                    _mainWindow = Window.GetWindow(this);

                return _mainWindow;
            }
        }

        private ScrollViewer ParentScrollViewer { get; set; }

        [DllImport("User32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    }
}