#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public static class Extensions {

        public static IEnumerable<IEnumerable<T>> Partition<T>(this IEnumerable<T> list, int size) {
            while (list.Any()) { yield return list.Take(size); list = list.Skip(size); }
        }
    }
}