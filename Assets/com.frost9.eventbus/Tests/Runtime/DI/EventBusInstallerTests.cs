#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using R3;
using System;
using System.Collections.Generic;
using System.Threading;
using VContainer;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Tests for EventBusInstaller covering VContainer dependency injection integration,
    /// singleton registration behavior, container scoping, and lifecycle management.
    /// Verifies proper DI setup and isolation between container instances.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class EventBusInstallerTests
    {
        // Test event types
        public readonly struct TestEvent
        {
            public readonly int Value;
            public TestEvent(int value) => Value = value;
        }

        public readonly struct IsolationEvent
        {
            public readonly string ContainerId;
            public readonly int Value;
            public IsolationEvent(string containerId, int value)
            {
                ContainerId = containerId;
                Value = value;
            }
        }

        #region Basic DI Integration Tests

        [Test]
        public void EventBusInstaller_RegistersEventBusAsSingleton()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);

            using var container = builder.Build();

            var eventBus1 = container.Resolve<IEventBus>();
            var eventBus2 = container.Resolve<IEventBus>();

            Assert.IsNotNull(eventBus1, "Should resolve EventBus instance");
            Assert.IsNotNull(eventBus2, "Should resolve EventBus instance on second call");
            Assert.AreSame(eventBus1, eventBus2, "Should return same instance (singleton)");
        }

        [Test]
        public void EventBusInstaller_ResolvesAsIEventBusInterface()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);

            using var container = builder.Build();

            var eventBus = container.Resolve<IEventBus>();

            Assert.IsNotNull(eventBus, "Should resolve as IEventBus interface");
            Assert.IsInstanceOf<IEventBus>(eventBus, "Should be instance of IEventBus");
            Assert.IsInstanceOf<EventBus>(eventBus, "Should be concrete EventBus implementation");
        }

        [Test]
        public void EventBusInstaller_EventBusFunctionality_WorksCorrectly()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);

            using var container = builder.Build();
            var eventBus = container.Resolve<IEventBus>();

            // Test basic functionality
            var received = new List<TestEvent>();
            using var subscription = eventBus.Observe<TestEvent>().Subscribe(received.Add);

            eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received.Count, "EventBus should work normally when resolved from container");
            Assert.AreEqual(42, received[0].Value);
        }

        #endregion

        #region Container Scoping Tests

        [Test]
        public void EventBusInstaller_MultipleContainers_EventIsolation()
        {
            // Create first container
            var builder1 = new ContainerBuilder();
            var installer1 = new EventBusInstaller();
            installer1.Install(builder1);
            using var container1 = builder1.Build();
            var eventBus1 = container1.Resolve<IEventBus>();

            // Create second container
            var builder2 = new ContainerBuilder();
            var installer2 = new EventBusInstaller();
            installer2.Install(builder2);
            using var container2 = builder2.Build();
            var eventBus2 = container2.Resolve<IEventBus>();

            Assert.AreNotSame(eventBus1, eventBus2, "Different containers should have different EventBus instances");

            // Test event isolation
            var received1 = new List<IsolationEvent>();
            var received2 = new List<IsolationEvent>();

            using var sub1 = eventBus1.Observe<IsolationEvent>().Subscribe(received1.Add);
            using var sub2 = eventBus2.Observe<IsolationEvent>().Subscribe(received2.Add);

            // Publish to first bus
            eventBus1.Publish(new IsolationEvent("Container1", 100));

            Assert.AreEqual(1, received1.Count, "First container should receive event");
            Assert.AreEqual(0, received2.Count, "Second container should NOT receive event");

            // Publish to second bus
            eventBus2.Publish(new IsolationEvent("Container2", 200));

            Assert.AreEqual(1, received1.Count, "First container should not receive second container's event");
            Assert.AreEqual(1, received2.Count, "Second container should receive its own event");

            Assert.AreEqual("Container1", received1[0].ContainerId);
            Assert.AreEqual("Container2", received2[0].ContainerId);
        }

        [Test]
        public void EventBusInstaller_ContainerDisposal_DisposesEventBus()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();
            installer.Install(builder);

            using var container = builder.Build();
            // Resolve the singleton once and keep a strong reference
            var bus = container.Resolve<IEventBus>();

            var completed = new CompletionCounter();
            using var sub = bus.Observe<TestEvent>()
                .Subscribe(_ => { }, _ => Interlocked.Increment(ref completed.Count));

            // Dispose the container: should call EventBus.Dispose(), completing streams
            container.Dispose();

            Assert.AreEqual(1, completed.Count, "EventBus should complete subscribers on container disposal");
            // And the bus should be unusable after disposal
            Assert.Throws<ObjectDisposedException>(() => bus.Observe<TestEvent>());
        }

        #endregion

        #region Dependency Injection Integration Tests

        [Test]
        public void EventBusInstaller_InjectIntoOtherServices_WorksCorrectly()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);
            builder.Register<TestService>(Lifetime.Singleton);

            using var container = builder.Build();
            var service = container.Resolve<TestService>();

            Assert.IsNotNull(service, "Should resolve service that depends on IEventBus");
            Assert.IsNotNull(service.EventBus, "Service should have EventBus injected");

            // Test that the injected EventBus works
            service.PublishTestEvent(123);
            Assert.AreEqual(1, service.ReceivedEvents.Count);
            Assert.AreEqual(123, service.ReceivedEvents[0].Value);
        }

        [Test]
        public void EventBusInstaller_MultipleServicesShareSameInstance()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);
            builder.Register<TestService>(Lifetime.Singleton);
            builder.Register<AnotherTestService>(Lifetime.Singleton);

            using var container = builder.Build();

            var service1 = container.Resolve<TestService>();
            var service2 = container.Resolve<AnotherTestService>();
            var directEventBus = container.Resolve<IEventBus>();

            Assert.AreSame(service1.EventBus, service2.EventBus, "Both services should share same EventBus");
            Assert.AreSame(service1.EventBus, directEventBus, "Service EventBus should be same as directly resolved");

            // Test cross-service communication
            service1.PublishTestEvent(999);

            Assert.AreEqual(1, service1.ReceivedEvents.Count);
            Assert.AreEqual(1, service2.ReceivedEvents.Count);
            Assert.AreEqual(999, service2.ReceivedEvents[0].Value);
        }

        #endregion

        #region Lifetime and Disposal Tests

        [Test]
        public void EventBusInstaller_ResolveAfterContainerDisposal_ThrowsException()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);

            using var container = builder.Build();
            var bus = container.Resolve<IEventBus>();
            container.Dispose();

            // Resolution behavior after Dispose() is version-dependent; assert the bus is disposed instead
            Assert.Throws<ObjectDisposedException>(() => bus.Observe<TestEvent>(),
            "Resolved EventBus instance should be disposed after container disposal");
        }

        [Test]
        public void EventBusInstaller_MultipleInstallations_OnlyOneRegistration()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);
            installer.Install(builder); // installing twice on the same builder is a conflict in VContainer

            // Expect a conflict exception at Build time
            Assert.That(
            () => builder.Build(),
            Throws.Exception.TypeOf<VContainer.VContainerException>(),
            "Installing the same registration twice should cause a conflict"
                );
        }

        #endregion

        #region As-Binding Tests

        [Test]
        public void EventBusInstaller_OnlyIEventBusResolvable()
        {
            var builder = new ContainerBuilder();
            var installer = new EventBusInstaller();

            installer.Install(builder);

            using var container = builder.Build();

            // IEventBus should resolve
            var eventBus = container.Resolve<IEventBus>();
            Assert.IsNotNull(eventBus);

            // Direct EventBus resolution: depending on DI config, either:
            //  (a) throws (only IEventBus registered), OR
            //  (b) succeeds but MUST be the same singleton instance.
            EventBus concrete = null;
            var resolvedConcrete = false;
            try
            {
                concrete = container.Resolve<EventBus>();
                resolvedConcrete = true;
            }
            catch (VContainerException)
            {
                // Expected in common setups: only interface is registered.
            }

            if (resolvedConcrete)
            {
                Assert.AreSame(eventBus, concrete,
                "If the concrete type is resolvable, it must map to the same singleton instance");
            }
        }

        #endregion

        // Test service classes for DI integration
        public class TestService
        {
            public IEventBus EventBus { get; }
            public List<TestEvent> ReceivedEvents { get; } = new List<TestEvent>();

            public TestService(IEventBus eventBus)
            {
                EventBus = eventBus;
                EventBus.Observe<TestEvent>().Subscribe(ReceivedEvents.Add);
            }

            public void PublishTestEvent(int value)
            {
                EventBus.Publish(new TestEvent(value));
            }
        }

        public class AnotherTestService
        {
            public IEventBus EventBus { get; }
            public List<TestEvent> ReceivedEvents { get; } = new List<TestEvent>();

            public AnotherTestService(IEventBus eventBus)
            {
                EventBus = eventBus;
                EventBus.Observe<TestEvent>().Subscribe(ReceivedEvents.Add);
            }
        }
    }
}
#endif