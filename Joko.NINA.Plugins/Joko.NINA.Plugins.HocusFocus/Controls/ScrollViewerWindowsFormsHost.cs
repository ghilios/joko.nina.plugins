#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    // https://stackoverflow.com/questions/14080580/scrollviewer-is-not-working-in-wpf-windowsformhost
    public class ScrollViewerWindowsFormsHost : WindowsFormsHost {

        public static readonly DependencyProperty ParentScrollViewerProperty = DependencyProperty.Register(
            "ParentScrollViewer",
            typeof(ScrollViewer),
            typeof(ScrollViewerWindowsFormsHost),
            new FrameworkPropertyMetadata(
                default(ScrollViewer),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnParentScrollViewerPropertyChanged));

        public ScrollViewer ParentScrollViewer {
            get { return (ScrollViewer)GetValue(ParentScrollViewerProperty); }
            set { SetValue(ParentScrollViewerProperty, value); }
        }

        private static void OnParentScrollViewerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ScrollViewerWindowsFormsHost)d;
            var newValue = (ScrollViewer)e.NewValue;
            thisControl.ParentScrollViewer = newValue;
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox) {
            base.OnWindowPositionChanged(rcBoundingBox);
            var scrollViewer = ParentScrollViewer ?? ImplicitParentScrollViewer;
            if (scrollViewer == null) {
                return;
            }

            OnWindowPositionChanged(ParentScrollViewer, rcBoundingBox);
        }

        private bool onWindowPositionChangedLastFailed = false;

        private void OnWindowPositionChanged(ScrollViewer parentScrollViewer, Rect rcBoundingBox) {
            var mainWindow = Window.GetWindow(this);
            try {
                GeneralTransform tr = parentScrollViewer.TransformToAncestor(mainWindow);
                var scrollRect = new Rect(new Size(parentScrollViewer.ViewportWidth, parentScrollViewer.ViewportHeight));
                scrollRect = tr.TransformBounds(scrollRect);

                var intersect = Rect.Intersect(scrollRect, rcBoundingBox);
                if (!intersect.IsEmpty) {
                    tr = mainWindow.TransformToDescendant(this);
                    intersect = tr.TransformBounds(intersect);
                }

                SetRegion(intersect);
                onWindowPositionChangedLastFailed = false;
            } catch (Exception e) {
                Logger.Error("Failed OnWindowPositionChanged", e);
                if (!onWindowPositionChangedLastFailed) {
                    Notification.ShowWarning("ScrollViewer FormsHost failed on window position change");
                    onWindowPositionChangedLastFailed = true;
                }
            }
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

        public ScrollViewer ImplicitParentScrollViewer { get; private set; }

        [DllImport("User32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    }
}