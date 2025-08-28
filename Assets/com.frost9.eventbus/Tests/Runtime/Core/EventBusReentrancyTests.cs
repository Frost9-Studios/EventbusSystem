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
    /// Reentrancy and recursive publish tests for EventBus covering scenarios where subscribers
    /// publish events during their own event handling. Tests verify safe reentrancy behavior,
    /// proper event ordering, and prevention of infinite recursion.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class EventBusReentrancyTests
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

        public readonly struct ChainEvent
        {
            public readonly int Step;
            public readonly int MaxSteps;
            public ChainEvent(int step, int maxSteps) { Step = step; MaxSteps = maxSteps; }
        }

        public readonly struct TriggerEvent
        {
            public readonly string Message;
            public TriggerEvent(string message) => Message = message;
        }

        public readonly struct ResponseEvent
        {
            public readonly string Response;
            public ResponseEvent(string response) => Response = response;
        }

        #region Basic Reentrancy Tests

        [Test]
        public void Publish_ReentrantSameType_BothEventsDelivered()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();
            bool innerPublishDone = false;

            // Subscriber A publishes another event of same type during handling
            using var sub1 = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                received1.Add(evt);
                if (evt.Value == 1 && !innerPublishDone)
                {
                    innerPublishDone = true;
                    _eventBus.Publish(new TestEvent(2)); // Inner publish
                }
            });

            // Subscriber B should receive both outer and inner events
            using var sub2 = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);

            _eventBus.Publish(new TestEvent(1)); // Outer publish

            Assert.AreEqual(2, received1.Count, "Subscriber A should receive both outer and inner events");
            Assert.AreEqual(2, received2.Count, "Subscriber B should receive both outer and inner events");

            // Verify both subscribers got both values
            Assert.Contains(1, received1.ConvertAll(e => e.Value));
            Assert.Contains(2, received1.ConvertAll(e => e.Value));
            Assert.Contains(1, received2.ConvertAll(e => e.Value));
            Assert.Contains(2, received2.ConvertAll(e => e.Value));
        }

        [Test]
        public void Publish_ReentrantDifferentType_CrossTypeDelivery()
        {
            var triggerReceived = new List<TriggerEvent>();
            var responseReceived = new List<ResponseEvent>();

            // TriggerEvent subscriber publishes ResponseEvent
            using var triggerSub = _eventBus.Observe<TriggerEvent>().Subscribe(evt =>
            {
                triggerReceived.Add(evt);
                _eventBus.Publish(new ResponseEvent($"Response to {evt.Message}"));
            });

            // ResponseEvent subscriber
            using var responseSub = _eventBus.Observe<ResponseEvent>().Subscribe(responseReceived.Add);

            _eventBus.Publish(new TriggerEvent("Hello"));

            Assert.AreEqual(1, triggerReceived.Count);
            Assert.AreEqual(1, responseReceived.Count);
            Assert.AreEqual("Hello", triggerReceived[0].Message);
            Assert.AreEqual("Response to Hello", responseReceived[0].Response);
        }

        #endregion

        #region Recursion Prevention Tests

        [Test]
        public void Publish_RecursiveDoesNotInfiniteLoop_Guarded()
        {
            var received = new List<TestEvent>();
            const int maxDepth = 100; // Guard against infinite recursion
            int publishCount = 0;

            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                received.Add(evt);
                publishCount++;

                if (publishCount <= maxDepth && evt.Value < 5) // Guard condition
                {
                    _eventBus.Publish(new TestEvent(evt.Value + 1));
                }
            });

            _eventBus.Publish(new TestEvent(1));

            Assert.LessOrEqual(received.Count, maxDepth + 1, "Should not exceed maximum recursion depth");
            Assert.Greater(received.Count, 1, "Should have recursive publishes");

            // Verify the sequence
            for (int i = 0; i < Math.Min(5, received.Count); i++)
            {
                Assert.AreEqual(i + 1, received[i].Value, $"Event {i} should have correct incremental value");
            }
        }

        [Test]
        public void Publish_ChainedEvents_ControlledRecursion()
        {
            var received = new List<ChainEvent>();

            using var subscription = _eventBus.Observe<ChainEvent>().Subscribe(evt =>
            {
                received.Add(evt);
                if (evt.Step < evt.MaxSteps)
                {
                    _eventBus.Publish(new ChainEvent(evt.Step + 1, evt.MaxSteps));
                }
            });

            _eventBus.Publish(new ChainEvent(1, 5));

            Assert.AreEqual(5, received.Count, "Should receive exactly 5 chained events");
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(i + 1, received[i].Step, $"Step {i + 1} should be correct");
                Assert.AreEqual(5, received[i].MaxSteps, "MaxSteps should be consistent");
            }
        }

        #endregion

        #region Event Ordering Tests

        [Test]
        public void Publish_NestedEvents_OrderingBehavior()
        {
            var executionOrder = new List<string>();

            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                executionOrder.Add($"Start-{evt.Value}");

                if (evt.Value == 1)
                {
                    _eventBus.Publish(new TestEvent(2));
                }
                else if (evt.Value == 2)
                {
                    _eventBus.Publish(new TestEvent(3));
                }

                executionOrder.Add($"End-{evt.Value}");
            });

            _eventBus.Publish(new TestEvent(1));

            // Verify execution order reflects nested calls
            Assert.AreEqual(6, executionOrder.Count);
            Assert.AreEqual("Start-1", executionOrder[0]);
            Assert.AreEqual("Start-2", executionOrder[1]);
            Assert.AreEqual("Start-3", executionOrder[2]);
            Assert.AreEqual("End-3", executionOrder[3]);
            Assert.AreEqual("End-2", executionOrder[4]);
            Assert.AreEqual("End-1", executionOrder[5]);
        }

        [Test]
        public void Publish_MultipleSubscribersWithReentrancy_AllReceiveEvents()
        {
            var subscriber1Events = new List<TestEvent>();
            var subscriber2Events = new List<TestEvent>();
            var subscriber3Events = new List<TestEvent>();
            bool reentrantPublishDone = false;

            // Subscriber 1: publishes reentrantly
            using var sub1 = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                subscriber1Events.Add(evt);
                if (evt.Value == 10 && !reentrantPublishDone)
                {
                    reentrantPublishDone = true;
                    _eventBus.Publish(new TestEvent(20));
                }
            });

            // Subscriber 2: passive receiver
            using var sub2 = _eventBus.Observe<TestEvent>().Subscribe(subscriber2Events.Add);

            // Subscriber 3: passive receiver
            using var sub3 = _eventBus.Observe<TestEvent>().Subscribe(subscriber3Events.Add);

            _eventBus.Publish(new TestEvent(10));

            // All subscribers should receive both events
            Assert.AreEqual(2, subscriber1Events.Count);
            Assert.AreEqual(2, subscriber2Events.Count);
            Assert.AreEqual(2, subscriber3Events.Count);

            // Verify they all got the same events
            var expectedValues = new[] { 10, 20 };
            CollectionAssert.AreEquivalent(expectedValues, subscriber1Events.ConvertAll(e => e.Value));
            CollectionAssert.AreEquivalent(expectedValues, subscriber2Events.ConvertAll(e => e.Value));
            CollectionAssert.AreEquivalent(expectedValues, subscriber3Events.ConvertAll(e => e.Value));
        }

        #endregion

        #region Complex Reentrancy Scenarios Tests

        [Test]
        public void Publish_MutuallyRecursiveEventTypes_ControlledInteraction()
        {
            var triggerCount = 0;
            var responseCount = 0;
            const int maxIterations = 3;

            using var triggerSub = _eventBus.Observe<TriggerEvent>().Subscribe(evt =>
            {
                triggerCount++;
                if (triggerCount <= maxIterations)
                {
                    _eventBus.Publish(new ResponseEvent($"Response-{triggerCount}"));
                }
            });

            using var responseSub = _eventBus.Observe<ResponseEvent>().Subscribe(evt =>
            {
                responseCount++;
                if (responseCount <= maxIterations - 1)
                {
                    _eventBus.Publish(new TriggerEvent($"Trigger-{responseCount}"));
                }
            });

            _eventBus.Publish(new TriggerEvent("Initial"));

            Assert.AreEqual(maxIterations, triggerCount, "Should have limited trigger events");
            Assert.AreEqual(maxIterations, responseCount, "Should have limited response events");
        }

        [Test]
        public void Publish_ReentrancyWithSubscriptionChanges_SafeBehavior()
        {
            var received = new List<TestEvent>();
            IDisposable dynamicSubscription = null;
            var dynamicReceived = new List<TestEvent>();

            using var mainSubscription = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                received.Add(evt);

                if (evt.Value == 1)
                {
                    // Create new subscription during event handling
                    dynamicSubscription = _eventBus.Observe<TestEvent>().Subscribe(dynamicReceived.Add); // .Subscribe(dynamicReceived.Add) SAME AS .Subscribe(evt => dynamicReceived.Add(evt));
                    //dynamicSubscription?.Dispose(); => dispose here means expectedDynamic = new[] { };
                    _eventBus.Publish(new TestEvent(2));
                    dynamicSubscription?.Dispose(); // => dispose here means expectedDynamic = new[] { 3, 2 };
                }
                else if (evt.Value == 2)
                {
                    //dynamicSubscription?.Dispose(); => dispose here means expectedDynamic = new[] {  };
                    _eventBus.Publish(new TestEvent(3));
                    //dynamicSubscription?.Dispose(); => dispose here means expectedDynamic = new[] { 3 };
                }
            });

            _eventBus.Publish(new TestEvent(1));

            Assert.AreEqual(3, received.Count, "Main subscription should receive all events");
            Assert.AreEqual(2, dynamicReceived.Count, "New subscriptions during dispatch receive reentrant events provided they are not disposed");

            var expectedMain = new[] { 1, 2, 3 };
            var expectedDynamic = new[] { 3, 2 };

            CollectionAssert.AreEqual(expectedMain, received.ConvertAll(e => e.Value));
            CollectionAssert.AreEqual(expectedDynamic, dynamicReceived.ConvertAll(e => e.Value));
        }

        #endregion

        #region Error Handling During Reentrancy Tests

        [Test]
        public void Publish_ExceptionInReentrantHandler_DoesNotBreakChain()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();
            bool throwingDone = false;

            // Subscriber 1: throws exception during reentrant publish
            using var sub1 = _eventBus.Observe<TestEvent>().SubscribeSafe(evt =>
            {
                received1.Add(evt);
                if (evt.Value == 1 && !throwingDone)
                {
                    throwingDone = true;
                    _eventBus.Publish(new TestEvent(2)); // This should work
                    throw new InvalidOperationException("Test exception during reentrancy");
                }
            });

            // Subscriber 2: normal handler
            using var sub2 = _eventBus.Observe<TestEvent>().Subscribe(received2.Add);

            // SubscribeSafe logs the exception — declare it to avoid "Unhandled log message"
            LogAssert.Expect(LogType.Exception, new Regex("Test exception during reentrancy", RegexOptions.IgnoreCase));


            _eventBus.Publish(new TestEvent(1));

            Assert.AreEqual(2, received1.Count, "Throwing subscriber should still receive reentrant event");
            Assert.AreEqual(2, received2.Count, "Normal subscriber should receive both events despite exception");
        }

        [Test]
        public void Publish_ReentrancyWithDisposedBus_SafeFailure()
        {
            var received = new List<TestEvent>();
            bool disposeCalled = false;

            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(evt =>
            {
                received.Add(evt);
                if (evt.Value == 1 && !disposeCalled)
                {
                    disposeCalled = true;
                    _eventBus.Dispose(); // Dispose during handling

                    // Attempt reentrant publish on disposed bus should be safe
                    Assert.DoesNotThrow(() => _eventBus.Publish(new TestEvent(2)));
                }
            });

            _eventBus.Publish(new TestEvent(1));

            Assert.AreEqual(1, received.Count, "Should only receive first event before disposal");
        }

        #endregion
    }
}
#endif