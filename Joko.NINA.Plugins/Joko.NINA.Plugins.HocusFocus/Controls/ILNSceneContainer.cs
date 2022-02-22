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
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class BrushChangedEventArgs : EventArgs {
        public Brush Brush { get; set; }
    }

    public class ILNSceneContainer {

        public ILNSceneContainer(Scene scene) {
            this.Scene = scene;
        }

        public event EventHandler<BrushChangedEventArgs> BackgroundChanged;

        public event EventHandler<BrushChangedEventArgs> ForegroundChanged;

        public Scene Scene { get; private set; }

        public void UpdateBackgroundBrush(Brush brush) {
            this.BackgroundChanged?.Invoke(this, new BrushChangedEventArgs() {
                Brush = brush
            });
        }

        public void UpdateForegroundBrush(Brush brush) {
            this.ForegroundChanged?.Invoke(this, new BrushChangedEventArgs() {
                Brush = brush
            });
        }
    }
}