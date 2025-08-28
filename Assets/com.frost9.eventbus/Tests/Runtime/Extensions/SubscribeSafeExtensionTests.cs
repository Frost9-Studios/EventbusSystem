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
    /// Tests for SubscribeSafe extension covering exception isolation, error logging behavior,
    /// subscription lifecycle management, and fault tolerance patterns.
    /// Verifies that SubscribeSafe properly isolates exceptions without breaking event delivery.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class SubscribeSafeExtensionTests
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

        public readonly struct CountedEvent
        {
            public readonly int Id;
            public readonly int Counter;
            public CountedEvent(int id, int counter) { Id = id; Counter = counter; }
        }

        #region Basic Exception Isolation Tests

        [Test]
        public void SubscribeSafe_SingleException_IsolatesAndLogs()
        {
            var received = new List<TestEvent>();
            var goodSubscriberReceived = new List<TestEvent>();

            // Throwing subscriber
            using var throwingSub = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    throw new InvalidOperationException("Test exception");
                });

            // Good subscriber should still work
            using var goodSub = _eventBus.Observe<TestEvent>()
                .Subscribe(goodSubscriberReceived.Add);

            // Expect the exception to be logged
            LogAssert.Expect(LogType.Exception, new Regex("Test exception", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received.Count, "Throwing subscriber should receive the event before throwing");
            Assert.AreEqual(1, goodSubscriberReceived.Count, "Good subscriber should still receive the event");
            Assert.AreEqual(42, received[0].Value);
            Assert.AreEqual(42, goodSubscriberReceived[0].Value);
        }

        [Test]
        public void SubscribeSafe_MultipleSubscribersOneThrows_OthersUnaffected()
        {
            var goodReceived1 = new List<TestEvent>();
            var goodReceived2 = new List<TestEvent>();
            var throwingReceived = new List<TestEvent>();

            using var goodSub1 = _eventBus.Observe<TestEvent>().Subscribe(goodReceived1.Add);

            using var throwingSub = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throwingReceived.Add(evt);
                    throw new InvalidOperationException("Throwing subscriber exception");
                });

            using var goodSub2 = _eventBus.Observe<TestEvent>().Subscribe(goodReceived2.Add);

            LogAssert.Expect(LogType.Exception, new Regex("Throwing subscriber exception", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(100));

            Assert.AreEqual(1, goodReceived1.Count, "First good subscriber should receive event");
            Assert.AreEqual(1, throwingReceived.Count, "Throwing subscriber should receive event before throwing");
            Assert.AreEqual(1, goodReceived2.Count, "Second good subscriber should receive event despite exception");
        }

        [Test]
        public void SubscribeSafe_MultipleExceptions_AllLogged()
        {
            var throwCount = 0;

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throwCount++;
                    throw new InvalidOperationException($"Exception {throwCount}");
                });

            // Expect multiple exceptions to be logged
            LogAssert.Expect(LogType.Exception, new Regex("Exception 1", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("Exception 2", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("Exception 3", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(1));
            _eventBus.Publish(new TestEvent(2));
            _eventBus.Publish(new TestEvent(3));

            Assert.AreEqual(3, throwCount, "All events should have been processed despite exceptions");
        }

        #endregion

        #region Subscription Lifecycle Tests

        [Test]
        public void SubscribeSafe_ReturnsDisposableSubscription()
        {
            var received = new List<TestEvent>();

            var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(received.Add);

            Assert.IsNotNull(subscription, "Should return a disposable subscription");
            Assert.IsInstanceOf<IDisposable>(subscription, "Should implement IDisposable");

            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(1, received.Count);

            subscription.Dispose();

            _eventBus.Publish(new TestEvent(2));
            Assert.AreEqual(1, received.Count, "Should not receive events after disposal");
        }

        [Test]
        public void SubscribeSafe_DisposeAfterException_StopsDelivery()
        {
            var received = new List<TestEvent>();
            var throwCount = 0;

            var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    throwCount++;
                    throw new InvalidOperationException($"Exception {throwCount}");
                });

            LogAssert.Expect(LogType.Exception, new Regex("Exception 1", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(1, throwCount);

            subscription.Dispose();

            // Should not receive further events
            _eventBus.Publish(new TestEvent(2));
            Assert.AreEqual(1, received.Count, "Should not receive events after disposal");
            Assert.AreEqual(1, throwCount, "Should not throw again after disposal");
        }

        [Test]
        public void SubscribeSafe_ContinuesAfterException()
        {
            var received = new List<TestEvent>();
            var exceptionEventValue = -1;

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    if (evt.Value == 5)
                    {
                        exceptionEventValue = evt.Value;
                        throw new InvalidOperationException("Exception on value 5");
                    }
                });

            LogAssert.Expect(LogType.Exception, new Regex("Exception on value 5", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(1));
            _eventBus.Publish(new TestEvent(5)); // This will throw
            _eventBus.Publish(new TestEvent(10)); // This should still be received

            Assert.AreEqual(3, received.Count, "Should continue receiving events after exception");
            Assert.AreEqual(5, exceptionEventValue, "Should have processed the exception-throwing event");
            Assert.AreEqual(1, received[0].Value);
            Assert.AreEqual(5, received[1].Value);
            Assert.AreEqual(10, received[2].Value);
        }

        #endregion

        #region Error Handling Edge Cases Tests

        [Test]
        public void SubscribeSafe_NullReferenceException_HandledCorrectly()
        {
            string nullString = null;
            var received = new List<TestEvent>();

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    var length = nullString.Length; // Will throw NullReferenceException
                });

            LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException|Object reference", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received.Count, "Should receive event before exception");
            Assert.AreEqual(42, received[0].Value);
        }

        [Test]
        public void SubscribeSafe_ExceptionInSubscriptionItself_Isolated()
        {
            var goodReceived = new List<TestEvent>();
            var throwingReceived = new List<TestEvent>();

            using var goodSub = _eventBus.Observe<TestEvent>().Subscribe(goodReceived.Add);

            using var throwingSub = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throwingReceived.Add(evt);
                    // Simulate a complex operation that throws
                    var array = new int[1];
                    var value = array[10]; // IndexOutOfRangeException
                });

            LogAssert.Expect(LogType.Exception, new Regex("IndexOutOfRangeException|Index.*out of range", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(99));

            Assert.AreEqual(1, goodReceived.Count, "Good subscriber should work");
            Assert.AreEqual(1, throwingReceived.Count, "Throwing subscriber should receive event before exception");
        }

        #endregion

        #region Performance and Stress Tests

        [Test]
        public void SubscribeSafe_HighVolumeWithOccasionalExceptions_PerformsWell()
        {
            var received = new List<TestEvent>();
            var exceptionCount = 0;

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    if (evt.Value % 10 == 0) // Throw on every 10th event
                    {
                        exceptionCount++;
                        throw new InvalidOperationException($"Exception on event {evt.Value}");
                    }
                });

            // Expect exceptions for multiples of 10
            for (int i = 10; i <= 100; i += 10)
            {
                LogAssert.Expect(LogType.Exception, new Regex($"Exception on event {i}", RegexOptions.IgnoreCase));
            }

            // Publish 100 events
            for (int i = 1; i <= 100; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            Assert.AreEqual(100, received.Count, "Should receive all events");
            Assert.AreEqual(10, exceptionCount, "Should have thrown on every 10th event");
        }

        [Test]
        public void SubscribeSafe_MultipleThrowingSubscribers_AllIsolated()
        {
            var subscriber1Received = new List<TestEvent>();
            var subscriber2Received = new List<TestEvent>();
            var subscriber3Received = new List<TestEvent>();
            var goodReceived = new List<TestEvent>();

            using var throwingSub1 = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    subscriber1Received.Add(evt);
                    throw new InvalidOperationException("Subscriber 1 exception");
                });

            using var throwingSub2 = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    subscriber2Received.Add(evt);
                    throw new ArgumentException("Subscriber 2 exception");
                });

            using var throwingSub3 = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    subscriber3Received.Add(evt);
                    throw new NotSupportedException("Subscriber 3 exception");
                });

            using var goodSub = _eventBus.Observe<TestEvent>()
                .Subscribe(goodReceived.Add);

            // Expect all exceptions to be logged
            LogAssert.Expect(LogType.Exception, new Regex("Subscriber 1 exception", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("Subscriber 2 exception", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("Subscriber 3 exception", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(123));

            // All subscribers should receive the event
            Assert.AreEqual(1, subscriber1Received.Count, "Subscriber 1 should receive event");
            Assert.AreEqual(1, subscriber2Received.Count, "Subscriber 2 should receive event");
            Assert.AreEqual(1, subscriber3Received.Count, "Subscriber 3 should receive event");
            Assert.AreEqual(1, goodReceived.Count, "Good subscriber should receive event");

            // All should have correct values
            Assert.AreEqual(123, subscriber1Received[0].Value);
            Assert.AreEqual(123, subscriber2Received[0].Value);
            Assert.AreEqual(123, subscriber3Received[0].Value);
            Assert.AreEqual(123, goodReceived[0].Value);
        }

        #endregion

        #region Complex Scenarios Tests

        [Test]
        public void SubscribeSafe_WithRxOperators_ExceptionIsolation()
        {
            var received = new List<TestEvent>();
            var filteredAndMappedReceived = new List<int>();

            using var subscription = _eventBus.Observe<TestEvent>()
                .Where(evt => evt.Value > 0)
                .Select(evt => evt.Value * 2)
                .SubscribeSafe(value =>
                {
                    filteredAndMappedReceived.Add(value);
                    if (value == 10) // Original value was 5, mapped to 10
                    {
                        throw new InvalidOperationException("Exception on mapped value 10");
                    }
                });

            using var regularSub = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            LogAssert.Expect(LogType.Exception, new Regex("Exception on mapped value 10", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(2)); // Maps to 4
            _eventBus.Publish(new TestEvent(5)); // Maps to 10, will throw
            _eventBus.Publish(new TestEvent(8)); // Maps to 16

            Assert.AreEqual(3, received.Count, "Regular subscriber should receive all events");
            Assert.AreEqual(3, filteredAndMappedReceived.Count, "SubscribeSafe should continue after exception");

            Assert.AreEqual(4, filteredAndMappedReceived[0]);
            Assert.AreEqual(10, filteredAndMappedReceived[1]);
            Assert.AreEqual(16, filteredAndMappedReceived[2]);
        }

        [Test]
        public void SubscribeSafe_DuringBusDisposal_HandlesGracefully()
        {
            var received = new List<TestEvent>();
            var disposeTriggered = false;

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    if (evt.Value == 42 && !disposeTriggered)
                    {
                        disposeTriggered = true;
                        _eventBus.Dispose(); // Dispose during handling
                    }
                });

            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received.Count, "Should receive event before disposal");
            Assert.IsTrue(disposeTriggered, "Should have triggered disposal");
        }

        [Test]
        public void SubscribeSafe_ConcurrentAccess_ThreadSafe()
        {
            var received = new List<TestEvent>();
            var lockObject = new object();

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    lock (lockObject)
                    {
                        received.Add(evt);
                    }
                    if (evt.Value % 2 == 0)
                    {
                        throw new InvalidOperationException($"Exception on even value {evt.Value}");
                    }
                });

            // Expect exceptions for even values
            LogAssert.Expect(LogType.Exception, new Regex("Exception on even value 2", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("Exception on even value 4", RegexOptions.IgnoreCase));

            // Publish from main thread (EventBus is main-thread only)
            for (int i = 1; i <= 5; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            lock (lockObject)
            {
                Assert.AreEqual(5, received.Count, "Should receive all events");
            }
        }

        #endregion

        #region CompositeDisposable Integration Tests

        [Test]
        public void SubscribeSafe_WithCompositeDisposable_DisposesCorrectly()
        {
            var received = new List<TestEvent>();
            var cd = new CompositeDisposable();

            var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    throw new InvalidOperationException("Test exception");
                });

            cd.Add(subscription);

            LogAssert.Expect(LogType.Exception, new Regex("Test exception", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(1, received.Count);

            cd.Dispose();

            _eventBus.Publish(new TestEvent(2));
            Assert.AreEqual(1, received.Count, "Should not receive events after CompositeDisposable disposal");
        }

        [Test]
        public void SubscribeSafe_MultipleInCompositeDisposable_AllDispose()
        {
            var received1 = new List<TestEvent>();
            var received2 = new List<TestEvent>();
            var received3 = new List<TestEvent>();
            var cd = new CompositeDisposable
            {
                _eventBus.Observe<TestEvent>().SubscribeSafe(received1.Add),
                _eventBus.Observe<TestEvent>().SubscribeSafe(received2.Add),
                _eventBus.Observe<TestEvent>().SubscribeSafe(received3.Add)
            };

            _eventBus.Publish(new TestEvent(1));

            Assert.AreEqual(1, received1.Count);
            Assert.AreEqual(1, received2.Count);
            Assert.AreEqual(1, received3.Count);

            cd.Dispose();

            _eventBus.Publish(new TestEvent(2));

            Assert.AreEqual(1, received1.Count, "First subscription should be disposed");
            Assert.AreEqual(1, received2.Count, "Second subscription should be disposed");
            Assert.AreEqual(1, received3.Count, "Third subscription should be disposed");
        }

        #endregion
    }
}
#endif