using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class AlglibAPITests {

        [Test]
        public void MinLmCreateV_AllocatesState_AndDeallocatesCleanly() {
            var api = new AlglibAPI();
            var x = new double[] { 1.0, 2.0 };
            api.minlmcreatev(2, x, 1e-4, out var state);
            Assert.That(state, Is.Not.Null);
            api.deallocateimmediately(ref state);
        }

        [Test]
        public void MinLmCreateVj_AllocatesState_AndDeallocatesCleanly() {
            var api = new AlglibAPI();
            var x = new double[] { 1.0, 2.0 };
            api.minlmcreatevj(2, x, out var state);
            Assert.That(state, Is.Not.Null);
            api.deallocateimmediately(ref state);
        }

        [Test]
        public void RbfCreate_AllocatesModelCleanly() {
            var api = new AlglibAPI();
            api.rbfcreate(nx: 2, ny: 1, out var model);
            Assert.That(model, Is.Not.Null);
            api.deallocateimmediately(ref model);
        }

        [Test]
        public void ParallelMinLmCreate_DoesNotThrow() {
            var api = new AlglibAPI();
            const int parallelism = 8;
            var tasks = new Task[parallelism];
            for (var i = 0; i < parallelism; ++i) {
                tasks[i] = Task.Run(() => {
                    for (var j = 0; j < 50; ++j) {
                        var x = new double[] { 0.1, 0.2 };
                        api.minlmcreatev(2, x, 1e-4, out var state);
                        api.minlmsetcond(state, 1e-8, 100);
                        api.deallocateimmediately(ref state);
                    }
                });
            }
            Assert.DoesNotThrow(() => Task.WaitAll(tasks));
        }
    }
}
