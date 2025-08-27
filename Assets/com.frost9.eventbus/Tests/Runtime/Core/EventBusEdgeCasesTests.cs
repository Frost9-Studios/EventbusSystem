#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using R3;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Edge case tests for EventBus covering critical production scenarios including wrong-thread construction,
    /// subscription mutations during dispatch, exception continuity, nullability policies, and channel retention.
    /// These tests verify behavior that commonly causes issues in real-world usage.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class EventBusEdgeCasesTests
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

        public readonly struct EventWithId
        {
            public readonly int Id;
            public readonly int Value;
            public EventWithId(int id, int value) { Id = id; Value = value; }
        }

        public class TestReferenceEvent
        {
            public int Value { get; set; }
            public TestReferenceEvent(int value) => Value = value;
        }

        #region Wrong Thread Construction Tests

        [Test]
        public void EventBus_ConstructedOffMainThread_PolicyMisapplied()
        {
            EventBus offThreadBus = null;

            // Construct EventBus on a dedicated background thread (T-A)
            var constructed = new ManualResetEvent(false);
            var tA = new Thread(() =>
            {
                offThreadBus = new EventBus();
                constructed.Set();
            });
            tA.IsBackground = true;
            tA.Start();
            constructed.WaitOne();
            ThreadingHelpers.RunOnDedicatedThread(() => { offThreadBus = new EventBus(); });


            var received = new List<TestEvent>();
            using var subscription = offThreadBus.Observe<TestEvent>().Subscribe(received.Add);

            // Main thread publish should be ignored (policy captured from construction thread)
            offThreadBus.Publish(new TestEvent(42));
            Assert.AreEqual(0, received.Count, "Main thread publish should be ignored when bus constructed off main");

            // Publish from a different background thread (T-B) — also ignored
            var published = new ManualResetEvent(false);
            var tB = new Thread(() =>
            {
                offThreadBus.Publish(new TestEvent(100));
                published.Set();
            });
            tB.IsBackground = true;
            tB.Start();
            published.WaitOne();
            ThreadingHelpers.RunOnDedicatedThread(() => offThreadBus.Publish(new TestEvent(100)));


            Assert.AreEqual(0, received.Count, "Background publish from a non-construction thread should be ignored");

            offThreadBus?.Dispose();
        }


        #endregion

        #region Subscribe/Unsubscribe During Dispatch Tests

        [Test]
        public void Subscriber_DisposesDuringOnNext_DispatchContinues()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();
            IDisposable subscription1 = null;

            // First subscriber disposes itself during OnNext
            subscription1 = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                received1.Add(evt);
                subscription1?.Dispose();
            });

            using var subscription2 = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);

            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count, "Second subscriber should still receive event after first disposed itself");
        }

        [Test]
        public void Subscriber_SubscribesNewDuringOnNext_NewSubscriberDoesNotReceiveCurrentEvent()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();
            IDisposable newSubscription = null;

            // First subscriber creates new subscription during OnNext
            using var subscription1 = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                received1.Add(evt);
                newSubscription = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);
            });

            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(0, received2.Count, "New subscriber should not receive current event");

            // But should receive future events
            _eventBus.Publish(new TestEvent(100));
            Assert.AreEqual(2, received1.Count);
            Assert.AreEqual(1, received2.Count, "New subscriber should receive future events");

            newSubscription?.Dispose();
        }

        [Test]
        public void DisposeOfOneSubscription_DoesNotAffectOthers()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();
            var received3 = new List<TestEvent>();

            using var subscription1 = _eventBus.Observe<TestEvent>().Subscribe(received1.Add);
            var subscription2 = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);
            using var subscription3 = _eventBus.Observe<TestEvent>().Subscribe(received3.Add);

            // Dispose one subscription during dispatch
            using var disposerSubscription = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                subscription2.Dispose();
            });

            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count, "Subscription2 should receive current event before disposal");
            Assert.AreEqual(1, received3.Count);

            // Future events should not reach disposed subscription
            _eventBus.Publish(new TestEvent(100));
            Assert.AreEqual(2, received1.Count);
            Assert.AreEqual(1, received2.Count, "Disposed subscription should not receive future events");
            Assert.AreEqual(2, received3.Count);
        }

        #endregion

        #region Multiple Event Types Interleaving Tests

        [Test]
        public void Publish_InterleavedDifferentTypes_AllDelivered()
        {
            var testReceived = new List<TestEvent>();
            var idReceived = new List<EventWithId>();

            using var testSub = _eventBus.Observe<TestEvent>().Subscribe(testReceived.Add);
            using var idSub = _eventBus.Observe<EventWithId>().Subscribe(idReceived.Add);

            // Rapid sequence of different event types: A, B, A, B, A
            _eventBus.Publish(new TestEvent(1));           // A
            _eventBus.Publish(new EventWithId(1, 10));     // B
            _eventBus.Publish(new TestEvent(2));           // A
            _eventBus.Publish(new EventWithId(2, 20));     // B
            _eventBus.Publish(new TestEvent(3));           // A

            Assert.AreEqual(3, testReceived.Count, "TestEvent subscribers should receive all TestEvents");
            Assert.AreEqual(2, idReceived.Count, "EventWithId subscribers should receive all EventWithId events");

            // Verify order is maintained per type
            Assert.AreEqual(1, testReceived[0].Value);
            Assert.AreEqual(2, testReceived[1].Value);
            Assert.AreEqual(3, testReceived[2].Value);

            Assert.AreEqual(10, idReceived[0].Value);
            Assert.AreEqual(20, idReceived[1].Value);
        }

        #endregion

        #region Dispose Semantics Edge Cases

        [Test]
        public void EventBus_DisposeTwice_SecondCallIsNoOp()
        {
            var completedCount = 0;

            using var sub = _eventBus.Observe<TestEvent>()
                // positional args; completed takes a Result
                .Subscribe(_ => { }, _ => completedCount++);

            _eventBus.Dispose();
            Assert.AreEqual(1, completedCount);

            // Second dispose should be no-op
            Assert.DoesNotThrow(() => _eventBus.Dispose());
            Assert.AreEqual(1, completedCount, "OnCompleted should only be called once");
        }

        [Test]
        public void Dispose_SubscriberPublishesInOnCompleted_PublishIgnored()
        {
            var received = new List<TestEvent>();

            using var sub = _eventBus.Observe<TestEvent>()
                // onNext
                .Subscribe(
                    received.Add,
                    // onCompleted(Result _) — try to publish during completion
                    _ => _eventBus.Publish(new TestEvent(42))
                );

            _eventBus.Dispose();

            // Publish during completion should be ignored by a disposed bus
            Assert.AreEqual(0, received.Count);
        }

        [Test]
        public void SubscribeSafe_ExceptionDoesNotTearDownSubscription()
        {
            var received = new List<int>();
            var first = true;

            using var sub = _eventBus
                .Observe<TestEvent>()
                .SubscribeSafe(
                    onNext: e =>
                    {
                        if (first)
                        {
                            first = false;
                            throw new InvalidOperationException("boom");
                        }
                        received.Add(e.Value);
                    });

            // SubscribeSafe logs the thrown exception to Unity's log.
            // Expect exactly one Exception log containing "boom" before publishing.
            LogAssert.Expect(LogType.Exception, new Regex("boom"));

            // First publish triggers the exception (which is logged, not propagated)
            _eventBus.Publish(new TestEvent(1));

            // Second publish must still reach the same subscriber
            _eventBus.Publish(new TestEvent(2));

            Assert.AreEqual(1, received.Count, "Subscription should remain active after the first exception");
            Assert.AreEqual(2, received[0]);
        }



        #endregion

        #region Publish Semantics Edge Cases

        [Test]
        public void Publish_DefaultStructValue_StillDelivered()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            _eventBus.Publish(default(TestEvent));

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(0, received[0].Value); // default int value
        }

        [Test]
        public void Publish_NullReferenceValue_StillDelivered()
        {
            var received = new List<TestReferenceEvent>();
            using var subscription = _eventBus.Observe<TestReferenceEvent>().Subscribe(received.Add);

            _eventBus.Publish<TestReferenceEvent>(null);

            Assert.AreEqual(1, received.Count);
            Assert.IsNull(received[0]);
        }

        [Test]
        public void Publish_WithNoObservers_NoReplay()
        {
            // Publish without any prior Observe<T>()
            _eventBus.Publish(new TestEvent(42));

            // Later Observe should show no replay
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);
            Assert.AreEqual(0, received.Count);

            // Then publish should work normally
            _eventBus.Publish(new TestEvent(100));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(100, received[0].Value);
        }

        #endregion

        #region Filtering/Identity Edge Cases

        [Test]
        public void Observe_MultiplePredicates_OnlyMatchingExecute()
        {
            var flag1Executed = false;
            var flag2Executed = false;
            var flag3Executed = false;

            using var sub1 = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id == 1)
                .Subscribe(_ => flag1Executed = true);

            using var sub2 = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id == 2)
                .Subscribe(_ => flag2Executed = true);

            using var sub3 = _eventBus.Observe<EventWithId>()
                .Where(e => e.Id == 3)
                .Subscribe(_ => flag3Executed = true);

            _eventBus.Publish(new EventWithId(2, 100));

            Assert.IsFalse(flag1Executed, "ID=1 filter should not execute");
            Assert.IsTrue(flag2Executed, "ID=2 filter should execute");
            Assert.IsFalse(flag3Executed, "ID=3 filter should not execute");
        }

        #endregion

        #region Subject Retention Tests

        [Test]
        public void Observe_UnsubscribeAll_NoChannelPruning()
        {
            // Subscribe and then dispose all subscriptions
            var subscription = _eventBus.Observe<TestEvent>().Subscribe(_ => { });
            subscription.Dispose();

            // Channel should remain in dictionary - new subscription should work
            var received = new List<TestEvent>();
            using var newSubscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            _eventBus.Publish(new TestEvent(42));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(42, received[0].Value);
        }

        #endregion

        #region Idempotent Patterns Tests

        [Test]
        public void CompositeDisposable_DisposeTwice_NoErrors()
        {
            var cd = new CompositeDisposable();
            var subscription = _eventBus.Observe<TestEvent>().Subscribe(_ => { });
            cd.Add(subscription);

            Assert.DoesNotThrow(() => cd.Dispose());
            Assert.DoesNotThrow(() => cd.Dispose()); // Second dispose should be safe
        }

        [Test]
        public void Observe_MultipleSubscriptions_IndependentLifecycles()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();

            var sub1 = _eventBus.Observe<TestEvent>().Subscribe(received1.Add);
            var sub2 = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);

            // Dispose first subscription
            sub1.Dispose();

            // Second subscription should still work
            _eventBus.Publish(new TestEvent(42));
            Assert.AreEqual(0, received1.Count);
            Assert.AreEqual(1, received2.Count);

            sub2.Dispose();
        }

        #endregion
    }
}
#endif