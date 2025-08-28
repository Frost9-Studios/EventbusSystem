#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using R3;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Final edge case tests covering the most subtle EventBus behaviors that commonly cause
    /// production issues: SubscribeSafe exception continuity, Next race conditions, nullability policies,
    /// channel retention patterns, and subscription disposal cleanup verification.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class EventBusFinalEdgeCasesTests
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

        public class TestReferenceEvent
        {
            public int Value { get; set; }
            public TestReferenceEvent(int value) => Value = value;
        }

        public readonly struct EventWithId
        {
            public readonly int Id;
            public readonly int Value;
            public EventWithId(int id, int value) { Id = id; Value = value; }
        }

        #region SubscribeSafe Exception Continuity Tests

        [Test]
        public void SubscribeSafe_ExceptionDoesNotTearDownSubscription()
        {
            var received = new List<int>();
            bool firstEvent = true;

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    if (firstEvent)
                    {
                        firstEvent = false;
                        throw new InvalidOperationException("First event exception");
                    }
                    received.Add(evt.Value);
                });

            // Expect the exception to be logged by SubscribeSafe
            LogAssert.Expect(LogType.Exception, new Regex("First event exception", RegexOptions.IgnoreCase));

            // First event triggers exception
            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(0, received.Count, "First event should not be recorded due to exception");

            // Second event should still be received (subscription not torn down)
            _eventBus.Publish(new TestEvent(2));
            Assert.AreEqual(1, received.Count, "Subscription should remain active after exception");
            Assert.AreEqual(2, received[0], "Second event should be received correctly");

            // Third event should also work
            _eventBus.Publish(new TestEvent(3));
            Assert.AreEqual(2, received.Count, "Subscription should continue working");
            Assert.AreEqual(3, received[1], "Third event should be received correctly");
        }

        [Test]
        public void SubscribeSafe_MultipleExceptionsGetLogged()
        {
            var throwCount = 0;
            const int expectedThrows = 3;

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throwCount++;
                    throw new InvalidOperationException($"Exception {throwCount}");
                });

            // Expect multiple exceptions to be logged
            for (int i = 1; i <= expectedThrows; i++)
            {
                LogAssert.Expect(LogType.Exception, new Regex($"Exception {i}", RegexOptions.IgnoreCase));
            }

            // Publish multiple events that will all throw
            for (int i = 1; i <= expectedThrows; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            Assert.AreEqual(expectedThrows, throwCount, "All events should have been processed despite exceptions");
        }

        [Test]
        public void SubscribeSafe_DisposeReturnedSubscription_StopsFutureDeliveries()
        {
            var received = new List<TestEvent>();

            var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(received.Add);

            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(1, received.Count);

            // Dispose the subscription
            subscription.Dispose();

            // Future publishes should not reach the handler
            _eventBus.Publish(new TestEvent(2));
            Assert.AreEqual(1, received.Count, "Handler should not be invoked after subscription disposal");
        }

        #endregion

        #region Nullability Policy Tests

        [Test]
        public void Publish_NullReference_DeliveredCorrectly()
        {
            var received = new List<TestReferenceEvent>();
            using var subscription = _eventBus.Observe<TestReferenceEvent>().Subscribe(received.Add);

            // R3 should allow null reference events
            _eventBus.Publish<TestReferenceEvent>(null);

            Assert.AreEqual(1, received.Count, "Null reference should be delivered");
            Assert.IsNull(received[0], "Received event should be null");
        }

        [Test]
        public void Publish_NullableStruct_PolicyConsistent()
        {
            var received = new List<TestEvent?>();
            using var subscription = _eventBus.Observe<TestEvent?>().Subscribe(received.Add);

            // Publish null nullable struct
            _eventBus.Publish<TestEvent?>(null);

            Assert.AreEqual(1, received.Count, "Null nullable struct should be delivered");
            Assert.IsNull(received[0], "Received event should be null");

            // Publish non-null nullable struct
            _eventBus.Publish<TestEvent?>(new TestEvent(42));
            Assert.AreEqual(2, received.Count, "Non-null nullable struct should also be delivered");
            Assert.IsNotNull(received[1], "Second event should not be null");
            Assert.AreEqual(42, received[1].Value.Value, "Non-null value should be correct");
        }

        #endregion

        #region Channel Retention and Management Tests

        [Test]
        public void Observe_UnsubscribeAll_NoChannelPruning()
        {
            // Create and dispose multiple subscriptions
            var sub1 = _eventBus.Observe<TestEvent>().Subscribe(_ => { });
            var sub2 = _eventBus.Observe<TestEvent>().Subscribe(_ => { });
            var sub3 = _eventBus.Observe<TestEvent>().Subscribe(_ => { });

            sub1.Dispose();
            sub2.Dispose();
            sub3.Dispose();

            // Channel should remain (no pruning) - new subscription should work
            var received = new List<TestEvent>();
            using var newSubscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            _eventBus.Publish(new TestEvent(42));
            Assert.AreEqual(1, received.Count, "Channel should still exist after all subscriptions disposed");
            Assert.AreEqual(42, received[0].Value, "New subscription should work normally");
        }

        [Test]
        public void EventBus_ManySubscriptions_ChannelUsageStable()
        {
            var subscriptions = new List<IDisposable>();
            const int eventTypeCount = 50;

            try
            {
                // Create channels for many different event types
                for (int i = 0; i < eventTypeCount; i++)
                {
                    var sub = _eventBus.Observe<EventWithId>()
                        .Where(e => e.Id == i) // Different filter per "type"
                        .Subscribe(_ => { });
                    subscriptions.Add(sub);
                }

                // Dispose half of them
                for (int i = 0; i < eventTypeCount / 2; i++)
                {
                    subscriptions[i].Dispose();
                }

                // Should still be able to create new subscriptions
                var testReceived = new List<EventWithId>();
                using var testSub = _eventBus.Observe<EventWithId>().Subscribe(testReceived.Add);

                _eventBus.Publish(new EventWithId(999, 42));
                Assert.AreEqual(1, testReceived.Count, "New subscription should work despite many disposed subscriptions");
            }
            finally
            {
                // Clean up remaining subscriptions
                foreach (var sub in subscriptions)
                {
                    sub?.Dispose();
                }
            }
        }

        #endregion

        #region Memory and Subscription Cleanup Tests

        [Test]
        public void Subscriptions_AreDisposed_ObjectsBecomesCollectable()
        {
            WeakReference weakRef = null;

            // Create scope to allow object to be collected
            CreateSubscriptionInScope();

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsFalse(weakRef?.IsAlive ?? false, "Subscription object should be collectible after disposal");

            void CreateSubscriptionInScope()
            {
                var received = new List<TestEvent>();
                var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);
                weakRef = new WeakReference(subscription);

                // Test that subscription works
                _eventBus.Publish(new TestEvent(1));
                Assert.AreEqual(1, received.Count);

                // Dispose subscription
                subscription.Dispose();
                subscription = null; // Without setting subscription = null;, the optimizer may consider it live until the end of the method, preventing collection even though you disposed it.
            }
        }

        [Test]
        public void CompositeDisposable_MultipleSubscriptions_AllDisposed()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();
            var received3 = new List<TestEvent>();

            var compositeDisposable = new CompositeDisposable
            {
                _eventBus.Observe<TestEvent>().Subscribe(received1.Add),
                _eventBus.Observe<TestEvent>().Subscribe(received2.Add),
                _eventBus.Observe<TestEvent>().Subscribe(received3.Add)
            };

            // Verify all subscriptions work
            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count);
            Assert.AreEqual(1, received3.Count);

            // Dispose all at once
            compositeDisposable.Dispose();

            // None should receive further events
            _eventBus.Publish(new TestEvent(2));
            Assert.AreEqual(1, received1.Count, "First subscription should be disposed");
            Assert.AreEqual(1, received2.Count, "Second subscription should be disposed");
            Assert.AreEqual(1, received3.Count, "Third subscription should be disposed");
        }

        #endregion

        #region Complex Filtering and Identity Tests

        [Test]
        public void Observe_ComplexPredicateChains_OnlyMatchingExecute()
        {
            var highValueReceived = new List<EventWithId>();
            var lowValueReceived = new List<EventWithId>();
            var evenIdReceived = new List<EventWithId>();
            var oddIdReceived = new List<EventWithId>();

            using var highValueSub = _eventBus.Observe<EventWithId>()
                .Where(e => e.Value >= 100)
                .Subscribe(highValueReceived.Add);

            using var lowValueSub = _eventBus.Observe<EventWithId>()
                .Where(e => e.Value < 100)
                .Subscribe(lowValueReceived.Add);

            using var evenIdSub = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id % 2 == 0)
                .Subscribe(evenIdReceived.Add);

            using var oddIdSub = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id % 2 == 1)
                .Subscribe(oddIdReceived.Add);

            // Publish events that should trigger different combinations
            _eventBus.Publish(new EventWithId(2, 150)); // High value, even ID
            _eventBus.Publish(new EventWithId(3, 50));  // Low value, odd ID
            _eventBus.Publish(new EventWithId(4, 75));  // Low value, even ID
            _eventBus.Publish(new EventWithId(5, 200)); // High value, odd ID

            Assert.AreEqual(2, highValueReceived.Count, "High value filter should match 2 events");
            Assert.AreEqual(2, lowValueReceived.Count, "Low value filter should match 2 events");
            Assert.AreEqual(2, evenIdReceived.Count, "Even ID filter should match 2 events");
            Assert.AreEqual(2, oddIdReceived.Count, "Odd ID filter should match 2 events");

            // Verify specific matches
            Assert.IsTrue(highValueReceived.Exists(e => e.Id == 2 && e.Value == 150));
            Assert.IsTrue(lowValueReceived.Exists(e => e.Id == 3 && e.Value == 50));
            Assert.IsTrue(evenIdReceived.Exists(e => e.Id == 4 && e.Value == 75));
            Assert.IsTrue(oddIdReceived.Exists(e => e.Id == 5 && e.Value == 200));
        }

        [Test]
        public void Observe_PerEntityFiltering_NoFalsePositives()
        {
            var entity1Events = new List<EventWithId>();
            var entity2Events = new List<EventWithId>();
            var entity3Events = new List<EventWithId>();

            using var entity1Sub = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id == 1)
                .Subscribe(entity1Events.Add);

            using var entity2Sub = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id == 2)
                .Subscribe(entity2Events.Add);

            using var entity3Sub = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id == 3)
                .Subscribe(entity3Events.Add);

            // Publish events for different entities
            _eventBus.Publish(new EventWithId(1, 100));
            _eventBus.Publish(new EventWithId(2, 200));
            _eventBus.Publish(new EventWithId(1, 101));
            _eventBus.Publish(new EventWithId(3, 300));
            _eventBus.Publish(new EventWithId(2, 201));

            // Verify isolation
            Assert.AreEqual(2, entity1Events.Count, "Entity 1 should receive exactly 2 events");
            Assert.AreEqual(2, entity2Events.Count, "Entity 2 should receive exactly 2 events");
            Assert.AreEqual(1, entity3Events.Count, "Entity 3 should receive exactly 1 event");

            // Verify no cross-contamination
            Assert.IsTrue(entity1Events.TrueForAll(e => e.Id == 1), "Entity 1 should only receive ID=1 events");
            Assert.IsTrue(entity2Events.TrueForAll(e => e.Id == 2), "Entity 2 should only receive ID=2 events");
            Assert.IsTrue(entity3Events.TrueForAll(e => e.Id == 3), "Entity 3 should only receive ID=3 events");
        }

        #endregion

        #region Error Isolation During Complex Operations Tests

        [Test]
        public void SubscribeSafe_IsolatesFaultsInComplexChain()
        {
            var goodReceived = new List<TestEvent>();
            var throwingCount = 0;

            // Good subscriber
            using var goodSub = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(goodReceived.Add);

            // Throwing subscriber
            var throwingSubscriber = new ThrowingSubscriber<TestEvent>(evt => evt.Value % 2 == 0); // Throw on even values
            using var throwingSub = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    try
                    {
                        throwingSubscriber.OnNext(evt);
                    }
                    finally
                    {
                        throwingCount++;
                    }
                });

            // Expect two SubscribeSafe-logged exceptions (even values 2 and 4).
            LogAssert.Expect(LogType.Exception, new Regex("Test exception for value:", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("Test exception for value:", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(1)); // Should not throw
            _eventBus.Publish(new TestEvent(2)); // Should throw
            _eventBus.Publish(new TestEvent(3)); // Should not throw
            _eventBus.Publish(new TestEvent(4)); // Should throw
            _eventBus.Publish(new TestEvent(5)); // Should not throw

            // Good subscriber should receive all events
            Assert.AreEqual(5, goodReceived.Count, "Good subscriber should receive all events");
            var expectedValues = new[] { 1, 2, 3, 4, 5 };
            CollectionAssert.AreEqual(expectedValues, goodReceived.ConvertAll(e => e.Value));

            // Throwing subscriber should have processed all events (even though some threw)
            Assert.AreEqual(5, throwingCount, "Throwing subscriber should process all events");
            Assert.AreEqual(2, throwingSubscriber.ThrowCount, "Should have thrown exactly twice");
        }

        #endregion
    }
}
#endif