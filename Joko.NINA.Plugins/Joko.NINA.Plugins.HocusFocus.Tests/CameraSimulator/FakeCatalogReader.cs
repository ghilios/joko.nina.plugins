using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// A deterministic in-memory <see cref="IAstapCatalogReader"/> for compositor/capstone tests. It ignores the
    /// query geometry (the compositor still projects + frames the stars) and returns a fixed set — or invokes a
    /// supplier that can throw, to exercise the compositor's starless-on-failure path.
    /// </summary>
    internal sealed class FakeCatalogReader : IAstapCatalogReader {
        private readonly Func<IEnumerable<CatalogStar>> supplier;

        public FakeCatalogReader(IEnumerable<CatalogStar> stars) {
            var list = new List<CatalogStar>(stars);
            supplier = () => list;
        }

        public FakeCatalogReader(Func<IEnumerable<CatalogStar>> supplier) {
            this.supplier = supplier ?? throw new ArgumentNullException(nameof(supplier));
        }

        public IEnumerable<CatalogStar> Query(double centerRaDeg, double centerDecDeg, double fovDeg, double limitingMagnitude) {
            return supplier();
        }
    }
}
