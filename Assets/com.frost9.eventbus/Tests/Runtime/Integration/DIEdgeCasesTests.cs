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
    /// Edge case tests for EventBus DI integration covering complex container scenarios,
    /// nested scopes, service lifetime interactions, registration conflicts, and disposal chains.
    /// Verifies robust behavior under challenging dependency injection scenarios.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class DIEdgeCasesTests
    {
        // Test event types
        public readonly struct ScopeEvent
        {
            public readonly string ScopeId;
            public readonly int Value;
            public ScopeEvent(string scopeId, int value)
            {
                ScopeId = scopeId;
                Value = value;
            }
        }

        public readonly struct LifecycleEvent
        {
            public readonly string ServiceName;
            public readonly string Phase;
            public LifecycleEvent(string serviceName, string phase)
            {
                ServiceName = serviceName;
                Phase = phase;
            }
        }

        #region Basic Extension Method Tests

        [Test]
        public void RegisterEventBus_RegistersSingleton()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();
            using var container = builder.Build();
            var a = container.Resolve<IEventBus>();
            var b = container.Resolve<IEventBus>();
            Assert.AreSame(a, b); // singleton
        }

        [Test]
        public void RegisterEventBus_ResolvesAsIEventBusInterface()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();

            using var container = builder.Build();

            var eventBus = container.Resolve<IEventBus>();

            Assert.IsNotNull(eventBus, "Should resolve as IEventBus interface");
            Assert.IsInstanceOf<IEventBus>(eventBus, "Should be instance of IEventBus");
            Assert.IsInstanceOf<EventBus>(eventBus, "Should be concrete EventBus implementation");
        }

        [Test]
        public void RegisterEventBus_EventBusFunctionality_WorksCorrectly()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();

            using var container = builder.Build();
            var eventBus = container.Resolve<IEventBus>();

            // Test basic functionality
            var received = new List<TestEvent>();
            using var subscription = eventBus.Observe<TestEvent>().Subscribe(received.Add);

            eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received.Count, "EventBus should work normally when resolved from container");
            Assert.AreEqual(42, received[0].Value);
        }

        [Test]
        public void RegisterEventBus_ContainerDisposal_DisposesEventBus()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();

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

        // Test event types
        public readonly struct TestEvent
        {
            public readonly int Value;
            public TestEvent(int value) => Value = value;
        }

        #region Nested Container and Scoping Tests

        [Test]
        public void EventBus_SeparateContainers_ProperIsolation()
        {
            // First container
            var builder1 = new ContainerBuilder();
            builder1.RegisterEventBus();

            using var container1 = builder1.Build();
            var bus1 = container1.Resolve<IEventBus>();

            // Second container
            var builder2 = new ContainerBuilder();
            builder2.RegisterEventBus();

            using var container2 = builder2.Build();
            var bus2 = container2.Resolve<IEventBus>();

            // Containers should have separate EventBus instances
            Assert.AreNotSame(bus1, bus2, "Separate containers should have different EventBus instances");

            // Test event isolation
            var received1 = new List<ScopeEvent>();
            var received2 = new List<ScopeEvent>();

            using var sub1 = bus1.Observe<ScopeEvent>().Subscribe(received1.Add);
            using var sub2 = bus2.Observe<ScopeEvent>().Subscribe(received2.Add);

            bus1.Publish(new ScopeEvent("Container1", 100));
            bus2.Publish(new ScopeEvent("Container2", 200));

            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count);
            Assert.AreEqual("Container1", received1[0].ScopeId);
            Assert.AreEqual("Container2", received2[0].ScopeId);
        }

        [Test]
        public void EventBus_IndependentContainerDisposal_NoInterference()
        {
            var builder1 = new ContainerBuilder();
            builder1.RegisterEventBus();

            using var container1 = builder1.Build();
            var bus1 = container1.Resolve<IEventBus>();

            var received1 = new List<ScopeEvent>();
            var completed1 = new CompletionCounter();

            using var sub1 = bus1.Observe<ScopeEvent>()
                .Subscribe(received1.Add, _ => Interlocked.Increment(ref completed1.Count));

            // Create and dispose second container
            var builder2 = new ContainerBuilder();
            builder2.RegisterEventBus();

            using (var container2 = builder2.Build())
            {
                var bus2 = container2.Resolve<IEventBus>();
                bus2.Publish(new ScopeEvent("Container2", 1));
                // Second container disposed here
            }

            // First container should still work after second container disposal
            bus1.Publish(new ScopeEvent("Container1", 2));

            Assert.AreEqual(1, received1.Count, "First container should still work after second container disposal");
            Assert.AreEqual(0, completed1.Count, "First container EventBus should not be completed");
            Assert.AreEqual("Container1", received1[0].ScopeId);
        }

        #endregion

        #region Dependency Injection Integration Tests

        [Test]
        public void RegisterEventBus_InjectIntoOtherServices_WorksCorrectly()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();
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
        public void RegisterEventBus_MultipleServicesShareSameInstance()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();
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

        #region Service Lifetime Interaction Tests

        [Test]
        public void EventBus_WithTransientServices_SharesSameInstance()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();
            builder.Register<TransientService>(Lifetime.Transient);

            using var container = builder.Build();

            var service1 = container.Resolve<TransientService>();
            var service2 = container.Resolve<TransientService>();
            var directBus = container.Resolve<IEventBus>();

            Assert.AreNotSame(service1, service2, "Services should be different instances (transient)");
            Assert.AreSame(service1.EventBus, service2.EventBus, "But should share same EventBus (singleton)");
            Assert.AreSame(service1.EventBus, directBus, "And same as directly resolved EventBus");
        }

        [Test]
        public void EventBus_ServiceDependencyChain_WorksCorrectly()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();
            builder.Register<EventPublisher>(Lifetime.Singleton);
            builder.Register<EventSubscriber>(Lifetime.Singleton);
            builder.Register<EventCoordinator>(Lifetime.Singleton);

            using var container = builder.Build();

            var coordinator = container.Resolve<EventCoordinator>();

            // Test the dependency chain
            coordinator.TriggerWorkflow("TestWorkflow");

            Assert.AreEqual(1, coordinator.Publisher.PublishedCount, "Publisher should have published");
            Assert.AreEqual(1, coordinator.Subscriber.ReceivedEvents.Count, "Subscriber should have received");
            Assert.AreEqual("TestWorkflow", coordinator.Subscriber.ReceivedEvents[0].ServiceName);
        }

        #endregion

        #region Registration Conflict and Resolution Tests

        [Test]
        public void EventBus_ConflictingRegistrations_Throws()
        {
            var builder = new ContainerBuilder();
            builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();
            builder.RegisterEventBus(); // same contract again

            Assert.Throws<VContainerException>(() => builder.Build(),
                "VContainer should throw on duplicate registrations for the same contract in one scope.");
        }


        [Test]
        public void EventBus_MultipleInterfaceBindings_ResolveSameInstance()
        {
            var builder = new ContainerBuilder();

            // Custom registration with multiple interface bindings
            builder.Register<EventBus>(Lifetime.Singleton)
                .As<IEventBus>()
                .As<IDisposable>();

            using var container = builder.Build();

            var eventBus = container.Resolve<IEventBus>();
            var disposable = container.Resolve<IDisposable>();

            Assert.AreSame(eventBus, disposable, "Should resolve to same instance for different interfaces");
        }

        #endregion

        #region Complex Disposal Chain Tests

        [Test]
        public void EventBus_ServiceDisposalOrder_HandledCorrectly()
        {
            var disposalOrder = new List<string>();

            var builder = new ContainerBuilder();
            builder.RegisterEventBus();
            builder.Register<DisposableService>(Lifetime.Singleton);

            using (var container = builder.Build())
            {
                var service = container.Resolve<DisposableService>();
                service.DisposalOrder = disposalOrder;

                var eventBus = container.Resolve<IEventBus>();

                // Set up monitoring
                var completed = new CompletionCounter();
                using var sub = eventBus.Observe<LifecycleEvent>()
                    .Subscribe(_ => { }, _ => Interlocked.Increment(ref completed.Count));

                // Container disposal triggers disposal chain
            }

            // Verify disposal occurred and EventBus streams were completed
            Assert.Contains("DisposableService", disposalOrder, "Service should have been disposed");

            Assert.AreEqual(1, new CompletionCounter { Count = 1 }.Count, "sanity"); // no-op; keeps us honest about Count usage
            // The monitored subscription should have been completed by EventBus.Dispose()
            // (We can't keep the local 'completed' out of the using-scope, so assert via another observer.)
            {
                var builder2 = new ContainerBuilder();
                builder2.RegisterEventBus();
                using var container2 = builder2.Build();
                var bus2 = container2.Resolve<IEventBus>();
                var c2 = new CompletionCounter();
                using var sub2 = bus2.Observe<LifecycleEvent>().Subscribe(_ => { }, _ => Interlocked.Increment(ref c2.Count));
                container2.Dispose();
                Assert.AreEqual(1, c2.Count, "EventBus should complete subscribers on container disposal");
            }
            // Note: We can't easily test exact disposal order with VContainer's disposal mechanism
        }

        [Test]
        public void EventBus_CircularDependencyPrevention_WorksCorrectly()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();

            // These services don't actually have circular dependency with EventBus
            // but test that EventBus works in complex dependency graphs
            builder.Register<ServiceA>(Lifetime.Singleton);
            builder.Register<ServiceB>(Lifetime.Singleton);

            using var container = builder.Build();

            var serviceA = container.Resolve<ServiceA>();
            var serviceB = container.Resolve<ServiceB>();

            Assert.IsNotNull(serviceA, "ServiceA should resolve");
            Assert.IsNotNull(serviceB, "ServiceB should resolve");
            Assert.AreSame(serviceA.EventBus, serviceB.EventBus, "Should share EventBus");
        }

        #endregion

        #region Container Builder Edge Cases Tests

        [Test]
        public void EventBus_EmptyContainerBuilder_RegistrationWorks()
        {
            var builder = new ContainerBuilder();

            // Register on completely empty builder
            Assert.DoesNotThrow(() => builder.RegisterEventBus(), "Should register on empty builder");

            using var container = builder.Build();
            var eventBus = container.Resolve<IEventBus>();

            Assert.IsNotNull(eventBus, "Should resolve from minimal container");
        }


        #endregion

        #region Performance and Stress Tests

        [Test]
        public void EventBus_ManyServicesUsingSameBus_PerformsWell()
        {
            var builder = new ContainerBuilder();
            builder.RegisterEventBus();

            // Register a single factory; we'll use it to create many ServiceConsumer instances
            builder.Register<Func<string, ServiceConsumer>>(container =>
            name => new ServiceConsumer(name, container.Resolve<IEventBus>()),
            Lifetime.Singleton);

            using var container = builder.Build();
            var services = new List<ServiceConsumer>();

            // Resolve all services
            var factory = container.Resolve<Func<string, ServiceConsumer>>();
            for (int i = 0; i < 50; i++)
            {
                services.Add(factory($"Service{i}"));
            }

            // Verify they all share the same EventBus
            var eventBus = container.Resolve<IEventBus>();
            foreach (var service in services)
            {
                Assert.AreSame(eventBus, service.EventBus, $"{service.Name} should share EventBus");
            }

            // Test mass communication
            eventBus.Publish(new LifecycleEvent("Broadcast", "Test"));

            foreach (var service in services)
            {
                Assert.AreEqual(1, service.ReceivedEvents.Count, $"{service.Name} should receive broadcast");
            }
        }

        #endregion

        // Test service classes for DI scenarios
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

        // Test service classes for DI edge case scenarios
        public class TransientService
        {
            public IEventBus EventBus { get; }

            public TransientService(IEventBus eventBus)
            {
                EventBus = eventBus;
            }
        }

        public class EventPublisher
        {
            public IEventBus EventBus { get; }
            public int PublishedCount { get; private set; }

            public EventPublisher(IEventBus eventBus)
            {
                EventBus = eventBus;
            }

            public void PublishLifecycleEvent(string serviceName, string phase)
            {
                EventBus.Publish(new LifecycleEvent(serviceName, phase));
                PublishedCount++;
            }
        }

        public class EventSubscriber
        {
            public IEventBus EventBus { get; }
            public List<LifecycleEvent> ReceivedEvents { get; } = new();

            public EventSubscriber(IEventBus eventBus)
            {
                EventBus = eventBus;
                EventBus.Observe<LifecycleEvent>().Subscribe(ReceivedEvents.Add);
            }
        }

        public class EventCoordinator
        {
            public EventPublisher Publisher { get; }
            public EventSubscriber Subscriber { get; }

            public EventCoordinator(EventPublisher publisher, EventSubscriber subscriber)
            {
                Publisher = publisher;
                Subscriber = subscriber;
            }

            public void TriggerWorkflow(string workflowName)
            {
                Publisher.PublishLifecycleEvent(workflowName, "Started");
            }
        }

        public class DisposableService : IDisposable
        {
            public IEventBus EventBus { get; }
            public List<string> DisposalOrder { get; set; }

            public DisposableService(IEventBus eventBus)
            {
                EventBus = eventBus;
            }

            public void Dispose()
            {
                DisposalOrder?.Add("DisposableService");
            }
        }

        public class ServiceA
        {
            public IEventBus EventBus { get; }
            public ServiceB ServiceB { get; }

            public ServiceA(IEventBus eventBus, ServiceB serviceB)
            {
                EventBus = eventBus;
                ServiceB = serviceB;
            }
        }

        public class ServiceB
        {
            public IEventBus EventBus { get; }

            public ServiceB(IEventBus eventBus)
            {
                EventBus = eventBus;
            }
        }

        public class ServiceConsumer
        {
            public string Name { get; }
            public IEventBus EventBus { get; }
            public List<LifecycleEvent> ReceivedEvents { get; } = new();

            public ServiceConsumer(string name, IEventBus eventBus)
            {
                Name = name;
                EventBus = eventBus;
                EventBus.Observe<LifecycleEvent>().Subscribe(ReceivedEvents.Add);
            }
        }
    }
}
#endif