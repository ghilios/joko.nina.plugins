using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using NINA.Sequencer.SequenceItem.Autofocus;

namespace NINA.Joko.Plugins.HocusFocus.Tests.SequenceItems {

    [TestFixture]
    public class RunAberrationInspectorTests {

        private static (RunAberrationInspector item, ICameraMediator camera, IFocuserMediator focuser, IFilterWheelMediator filterWheel, IInspectorVMFactory factory) Build(
            bool cameraConnected, bool focuserConnected, bool perFilterEnabled = false, bool filterWheelConnected = false) {
            var profileService = Substitute.For<IProfileService>();
            var cameraMediator = Substitute.For<ICameraMediator>();
            var focuserMediator = Substitute.For<IFocuserMediator>();
            var filterWheelMediator = Substitute.For<IFilterWheelMediator>();
            var inspectorVMFactory = Substitute.For<IInspectorVMFactory>();
            var perFilterStore = Substitute.For<IPerFilterStarDetectionStore>();

            cameraMediator.GetInfo().Returns(new CameraInfo { Connected = cameraConnected });
            focuserMediator.GetInfo().Returns(new FocuserInfo { Connected = focuserConnected });
            filterWheelMediator.GetInfo().Returns(new FilterWheelInfo { Connected = filterWheelConnected });
            perFilterStore.Enabled.Returns(perFilterEnabled);

            var item = new RunAberrationInspector(profileService, cameraMediator, focuserMediator, filterWheelMediator, inspectorVMFactory, perFilterStore);
            return (item, cameraMediator, focuserMediator, filterWheelMediator, inspectorVMFactory);
        }

        [Test]
        public void Validate_BothDevicesConnected_NoIssuesAndReturnsTrue() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                Assert.That(item.Issues, Is.Empty);
            });
        }

        [Test]
        public void Validate_CameraNotConnected_AddsCameraIssueAndReturnsFalse() {
            var (item, _, _, _, _) = Build(cameraConnected: false, focuserConnected: true);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                Assert.That(item.Issues, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void Validate_FocuserNotConnected_AddsFocuserIssueAndReturnsFalse() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: false);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                Assert.That(item.Issues, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void Validate_BothDisconnected_AddsTwoIssuesAndReturnsFalse() {
            var (item, _, _, _, _) = Build(cameraConnected: false, focuserConnected: false);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                Assert.That(item.Issues, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void Validate_RaisesPropertyChangedForIssues() {
            var (item, _, _, _, _) = Build(cameraConnected: false, focuserConnected: true);

            int issuesChanged = 0;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.Issues)) issuesChanged++; };

            item.Validate();

            Assert.That(issuesChanged, Is.EqualTo(1));
        }

        [Test]
        public void Clone_ProducesIndependentInstanceOfSameType() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true);
            item.Name = "Original";

            var clone = item.Clone() as RunAberrationInspector;

            Assert.Multiple(() => {
                Assert.That(clone, Is.Not.Null);
                Assert.That(clone, Is.Not.SameAs(item));
                Assert.That(clone!.Name, Is.EqualTo("Original"));
            });
        }

        [Test]
        public void ToString_IncludesItemName() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true);

            var s = item.ToString();

            Assert.That(s, Does.Contain(nameof(RunAberrationInspector)));
        }

        [Test]
        public async Task Execute_AlreadyCancelledToken_ThrowsOperationCanceled() {
            var (item, _, _, _, factory) = Build(cameraConnected: true, focuserConnected: true);
            // Factory returns null by default; that's fine because token check happens before deref.
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(async () => await item.Execute(progress: null, token: cts.Token),
                Throws.InstanceOf<System.OperationCanceledException>());
            await Task.CompletedTask;
        }

        [Test]
        public void Validate_PerFilterEnabledAndWheelDisconnected_AddsIssueAndReturnsFalse() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true, perFilterEnabled: true, filterWheelConnected: false);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                Assert.That(item.Issues, Has.Count.EqualTo(1));
                Assert.That(item.Issues[0], Is.EqualTo("Per-filter star detection requires a connected filter wheel"));
            });
        }

        [Test]
        public void Validate_PerFilterEnabledAndWheelConnected_NoIssuesAndReturnsTrue() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true, perFilterEnabled: true, filterWheelConnected: true);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                Assert.That(item.Issues, Is.Empty);
            });
        }

        [Test]
        public void Validate_PerFilterDisabled_WheelDisconnected_NoIssuesAndReturnsTrue() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true, perFilterEnabled: false, filterWheelConnected: false);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                Assert.That(item.Issues, Is.Empty);
            });
        }

        [Test]
        public void Validate_NullPerFilterStore_TreatedAsDisabled() {
            var cameraMediator = Substitute.For<ICameraMediator>();
            var focuserMediator = Substitute.For<IFocuserMediator>();
            cameraMediator.GetInfo().Returns(new CameraInfo { Connected = true });
            focuserMediator.GetInfo().Returns(new FocuserInfo { Connected = true });

            var item = new RunAberrationInspector(Substitute.For<IProfileService>(), cameraMediator, focuserMediator, Substitute.For<IFilterWheelMediator>(), Substitute.For<IInspectorVMFactory>(), perFilterStarDetectionStore: null);

            Assert.Multiple(() => {
                Assert.That(item.Validate(), Is.True);
                Assert.That(item.Issues, Is.Empty);
            });
        }
    }
}
