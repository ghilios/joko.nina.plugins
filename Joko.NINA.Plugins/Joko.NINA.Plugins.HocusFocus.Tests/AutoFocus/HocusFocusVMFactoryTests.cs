using NINA.Core.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class HocusFocusVMFactoryTests {

        private static HocusFocusVMFactory BuildFactory() {
            return new HocusFocusVMFactory(
                profileService: Substitute.For<IProfileService>(),
                focuserMediator: Substitute.For<IFocuserMediator>(),
                filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                autoFocusOptions: Substitute.For<IAutoFocusOptions>(),
                starDetectionOptions: Substitute.For<IStarDetectionOptions>(),
                autoFocusEngineFactory: Substitute.For<IAutoFocusEngineFactory>(),
                starDetectionSelector: Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
                alglibAPI: Substitute.For<IAlglibAPI>());
        }

        [Test]
        public void Name_IsHocusFocus() {
            var factory = BuildFactory();
            Assert.That(factory.Name, Is.EqualTo("Hocus Focus"));
        }

        [Test]
        public void ContentId_IsFullyQualifiedTypeName() {
            var factory = BuildFactory();
            Assert.That(factory.ContentId, Is.EqualTo(typeof(HocusFocusVMFactory).FullName));
        }

        [Test]
        public void Create_ReturnsHocusFocusVMInstance() {
            var factory = BuildFactory();

            var vm = factory.Create();

            Assert.Multiple(() => {
                Assert.That(vm, Is.Not.Null);
                Assert.That(vm, Is.InstanceOf<IAutoFocusVM>());
                Assert.That(vm, Is.InstanceOf<HocusFocusVM>());
            });
        }

        [Test]
        public void Create_ReturnsNewInstanceEachCall() {
            var factory = BuildFactory();

            var vm1 = factory.Create();
            var vm2 = factory.Create();

            Assert.That(vm1, Is.Not.SameAs(vm2));
        }
    }
}
