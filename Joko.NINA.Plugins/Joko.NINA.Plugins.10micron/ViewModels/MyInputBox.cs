#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.Utility;
using Joko.NINA.Plugins.TenMicron.View;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Joko.NINA.Plugins.TenMicron.ViewModels {

    public class MyInputBoxResult {

        public MyInputBoxResult(MessageBoxResult messageBoxResult, string inputText = "") {
            this.MessageBoxResult = messageBoxResult;
            this.InputText = inputText;
        }

        public MessageBoxResult MessageBoxResult { get; private set; }
        public string InputText { get; private set; }
    }

    public class MyInputBox : BaseINPC {
        private string title;

        public string Title {
            get => title;
            set {
                if (title != value) {
                    title = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string promptText;

        public string PromptText {
            get => promptText;
            set {
                if (promptText != value) {
                    promptText = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string promptTooltip;

        public string PromptTooltip {
            get => promptTooltip;
            set {
                if (promptTooltip != value) {
                    promptTooltip = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string inputText;

        public string InputText {
            get => inputText;
            set {
                if (inputText != value) {
                    inputText = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool? dialogResult;

        public bool? DialogResult {
            get => dialogResult;
            set {
                if (dialogResult != value) {
                    dialogResult = value;
                    RaisePropertyChanged();
                }
            }
        }

        public static MyInputBoxResult Show(string caption, string promptText, string promptTooltip) {
            var result = Application.Current.Dispatcher.Invoke(() => {
                var inputBox = new MyInputBox();
                inputBox.Title = caption;
                inputBox.PromptText = promptText;
                inputBox.PromptTooltip = promptTooltip;

                var mainWindow = Application.Current.MainWindow;
                var inputBoxWindow = new MyInputBoxView {
                    DataContext = inputBox
                };
                inputBoxWindow.ContentRendered += (object sender, EventArgs e) => {
                    var window = (System.Windows.Window)sender;
                    window.InvalidateVisual();

                    var rect = mainWindow.GetAbsolutePosition();
                    window.Left = rect.Left + (rect.Width - inputBoxWindow.ActualWidth) / 2;
                    window.Top = rect.Top + (rect.Height - inputBoxWindow.ActualHeight) / 2;
                };

                mainWindow.Opacity = 0.8;
                inputBoxWindow.Owner = mainWindow;
                inputBoxWindow.Closed += (object sender, EventArgs e) => {
                    Application.Current.MainWindow.Focus();
                };
                inputBoxWindow.ShowDialog();

                mainWindow.Opacity = 1;

                if (inputBoxWindow.DialogResult == null) {
                    return new MyInputBoxResult(MessageBoxResult.Cancel);
                } else if (inputBoxWindow.DialogResult == true) {
                    return new MyInputBoxResult(MessageBoxResult.OK, inputBox.InputText);
                } else {
                    return new MyInputBoxResult(MessageBoxResult.Cancel);
                }
            });
            return result;
        }
    }
}