#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.Exceptions;
using Joko.NINA.Plugins.TenMicron.Interfaces;
using NINA.Equipment.Interfaces.Mediator;

namespace Joko.NINA.Plugins.TenMicron.ModelBuilder {

    public class TelescopeMediatorMountCommander : IMountCommander {
        private readonly ITelescopeMediator telescopeMediator;

        public TelescopeMediatorMountCommander(ITelescopeMediator telescopeMediator) {
            this.telescopeMediator = telescopeMediator;
        }

        public void SendCommandBlind(string command, bool raw) {
            telescopeMediator.SendCommandBlind(command, raw);
        }

        public bool SendCommandBool(string command, bool raw) {
            return telescopeMediator.SendCommandBool(command, raw);
        }

        public string SendCommandString(string command, bool raw) {
            var result = telescopeMediator.SendCommandString(command, raw);
            if (result == null) {
                throw new CommandFailedException(command);
            }
            return result;
        }
    }
}