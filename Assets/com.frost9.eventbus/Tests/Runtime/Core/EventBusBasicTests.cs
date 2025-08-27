#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using R3;
using System;
using System.Collections.Generic;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Basic functionality tests for EventBus covering core API semantics, publish/subscribe behavior,
    /// event type isolation, disposal lifecycle, and fundamental reactive patterns.
    /// Tests run in EditMode and verify documented behavior without external dependencies.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class EventBusBasicTests
    {
        private EventBus _eventBus;

        [SetUp]
        public void SetUp()
        {
            _eventBus = new EventBus();
        }

        [TearDown]
        public void TearDown()
        {
            _eventBus?.Dispose();
        }

        // Test event types
        public readonly struct TestEvent
        {
            public readonly int Value;
            public TestEvent(int value) => Value = value;
        }

        public readonly struct HealthEvent
        {
            public readonly int Health;
            public HealthEvent(int health) => Health = health;
        }

        public readonly struct ManaEvent
        {
            public readonly int Mana;
            public ManaEvent(int mana) => Mana = mana;
        }

        public class TestReferenceEvent
        {
            public int Value { get; set; }
            public TestReferenceEvent(int value) => Value = value;
        }

        #region API & Basic Semantics Tests
        [Test]
        public void Observe_CreatesSubjectOnFirstCall()
        {

            //First call should create the observable
            R3.Observable<TestEvent> observable1 = _eventBus.Observe<TestEvent>();
            Assert.IsNotNull(observable1);

            // Second call should return an observable for the same channel
            var observable2 = _eventBus.Observe<TestEvent>();
            Assert.IsNotNull(observable2);

            // Verify both observables work by subscribing and publishing
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();

            using var sub1 = observable1.Subscribe(received1.Add);
            using var sub2 = observable2.Subscribe(received2.Add);

            var testEvent = new TestEvent(42);
            _eventBus.Publish(testEvent);

            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count);
            Assert.AreEqual(42, received1[0].Value);
            Assert.AreEqual(42, received2[0].Value);
        }

        [Test]
        public void Publish_BeforeAnyObserve_EventIsDropped()
        {
            // Publish before any observers
            _eventBus.Publish(new TestEvent(42));

            // Later observers should not receive the earlier published event
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>()
                .Subscribe(received.Add);

            // Should have received nothing from the earlier publish
            Assert.AreEqual(0, received.Count);

            // But should receive new events
            _eventBus.Publish(new TestEvent(100));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(100, received[0].Value);
        }

        [Test]
        public void Publish_WithMultipleSubscribers_AllReceiveEvent()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();

            using var sub1 = _eventBus.Observe<TestEvent>().Subscribe(received1.Add);
            using var sub2 = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);

            var testEvent = new TestEvent(42);
            _eventBus.Publish(testEvent);

            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count);
            Assert.AreEqual(42, received1[0].Value);
            Assert.AreEqual(42, received2[0].Value);
        }

        [Test]
        public void Publish_DifferentEventTypes_NoInterference()
        {
            var healthReceived = new List<HealthEvent>();
            var manaReceived = new List<ManaEvent>();

            using var healthSub = _eventBus.Observe<HealthEvent>().Subscribe(healthReceived.Add);
            using var manaSub = _eventBus.Observe<ManaEvent>().Subscribe(manaReceived.Add);

            // Publish health event
            _eventBus.Publish(new HealthEvent(100));
            Assert.AreEqual(1, healthReceived.Count);
            Assert.AreEqual(0, manaReceived.Count);

            // Publish mana event  
            _eventBus.Publish(new ManaEvent(50));
            Assert.AreEqual(1, healthReceived.Count);
            Assert.AreEqual(1, manaReceived.Count);

            Assert.AreEqual(100, healthReceived[0].Health);
            Assert.AreEqual(50, manaReceived[0].Mana);
        }

        #endregion

        #region Multi-Publisher Behavior Tests

        [Test]
        public void Publish_FromMultipleSources_AllEventsReceived()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Simulate two different publisher objects
            var publisher1 = _eventBus;
            var publisher2 = _eventBus; // Same instance, but conceptually different sources

            publisher1.Publish(new TestEvent(1));
            publisher2.Publish(new TestEvent(2));

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(1, received[0].Value);
            Assert.AreEqual(2, received[1].Value);
        }

        #endregion

        #region Struct vs Class Payload Tests

        [Test]
        public void Publish_StructPayload_DeliveredIntact()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            var originalEvent = new TestEvent(12345);
            _eventBus.Publish(originalEvent);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(12345, received[0].Value);
        }

        [Test]
        public void Publish_ClassPayload_SameReferenceDelivered()
        {
            var received = new List<TestReferenceEvent>();
            using var subscription = _eventBus.Observe<TestReferenceEvent>().Subscribe(received.Add);

            var originalEvent = new TestReferenceEvent(42);
            _eventBus.Publish(originalEvent);

            Assert.AreEqual(1, received.Count);
            Assert.AreSame(originalEvent, received[0]);
            Assert.AreEqual(42, received[0].Value);
        }

        #endregion

        #region Drop If Nobody Listening Tests

        [Test]
        public void Publish_WithNoObservers_NoReplay()
        {
            // Publish without any prior Observe<T>() calls
            _eventBus.Publish(new TestEvent(42));

            // Later observers should see no replay/buffering
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            Assert.AreEqual(0, received.Count);

            // But new publishes should work
            _eventBus.Publish(new TestEvent(100));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(100, received[0].Value);
        }

        #endregion

        #region Basic Disposal Tests

        [Test]
        public void Observe_AfterDispose_ThrowsObjectDisposedException()
        {
            _eventBus.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _eventBus.Observe<TestEvent>());
        }

        [Test]
        public void Publish_AfterDispose_NoDeliveryNoException()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            _eventBus.Dispose();

            // Should not throw
            Assert.DoesNotThrow(() => _eventBus.Publish(new TestEvent(42)));

            // Should not deliver
            Assert.AreEqual(0, received.Count);
        }

        #endregion

        #region Documentation Conformance Tests

        [Test]
        public void Publish_MultipleSubscribers_BothReceiveRegardlessOfOrder()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();

            using var sub1 = _eventBus.Observe<TestEvent>().Subscribe(received1.Add);
            using var sub2 = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);

            _eventBus.Publish(new TestEvent(42));

            // Both should receive (order unimportant per documentation)
            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count);
            Assert.AreEqual(42, received1[0].Value);
            Assert.AreEqual(42, received2[0].Value);
        }

        [Test]
        public void PublishThenObserve_NoReplay()
        {
            // Publish before observe
            _eventBus.Publish(new TestEvent(42));

            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Observer should see nothing (confirms no replay documentation)
            Assert.AreEqual(0, received.Count);
        }

        #endregion
    }
}
#endif