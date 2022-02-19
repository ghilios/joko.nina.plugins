#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILNumerics.Drawing;
using System;
using System.Windows;
using System.Windows.Controls;
using ILNPanel = ILNumerics.Drawing.Panel;
using WFPanel = System.Windows.Forms.Panel;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    /// <summary>
    /// Interaction logic for ILNCanvasControl.xaml
    /// </summary>
    public partial class ILNCanvasControl : UserControl {

        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
            "Scene",
            typeof(Scene),
            typeof(ILNCanvasControl),
            new FrameworkPropertyMetadata(
                default(Scene),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnScenePropertyChanged));

        public Scene Scene {
            get { return (Scene)GetValue(SceneProperty); }
            set { SetValue(SceneProperty, value); }
        }

        private static void OnScenePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNCanvasControl)d;
            var panel = thisControl.ILNPanel;
            panel.Scene = (Scene)e.NewValue;
            panel.Configure();
        }

        public ILNCanvasControl() {
            InitializeComponent();

            var wfPanel = (WFPanel)ILNumericsContainer.Child;
            wfPanel.SuspendLayout();
            ILNPanel = new ILNPanel();
            ILNPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            ILNPanel.Name = "ILNumericsPanel";
            ILNPanel.RendererType = ILNumerics.Drawing.RendererTypes.OpenGL;
            ILNPanel.ShowUIControls = false;
            wfPanel.Controls.Add(ILNPanel);
            wfPanel.ResumeLayout(true);
        }

        public ILNPanel ILNPanel;
    }
}