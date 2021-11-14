#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace Joko.NINA.Plugins.TenMicron.Interfaces {

    public interface IMountCommander {

        string SendCommandString(string command, bool raw);

        bool SendCommandBool(string command, bool raw);

        void SendCommandBlind(string command, bool raw);
    }
}