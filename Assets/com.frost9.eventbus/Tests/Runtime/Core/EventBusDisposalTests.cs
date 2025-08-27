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
    /// Disposal lifecycle and cleanup pattern tests for EventBus covering stream completion,
    /// cleanup behavior, memory management, and proper resource disposal.
    /// Tests verify that disposal is safe, idempotent, and properly completes all active streams.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class EventBusDisposalTests
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

        public readonly struct AnotherEvent
        {
            public readonly string Text;
            public AnotherEvent(string text) => Text = text;
        }

        #region Stream Completion Tests

        [Test]
        public void Dispose_CompletesAllActiveStreams()
        {
            var (sub1, values1, completed1) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();
            var (sub2, values2, completed2) = _eventBus.Observe<AnotherEvent>().SubscribeAndRecordWithCompletion();

            // Verify streams are active
            _eventBus.Publish(new TestEvent(1));
            _eventBus.Publish(new AnotherEvent("test"));

            Assert.AreEqual(1, values1.Count);
            Assert.AreEqual(1, values2.Count);
            Assert.AreEqual(0, completed1.Count, "Stream should not be completed yet");
            Assert.AreEqual(0, completed2.Count, "Stream should not be completed yet");

            // Dispose should complete all streams
            _eventBus.Dispose();

            Assert.AreEqual(1, completed1.Count, "Stream should be completed exactly once");
            Assert.AreEqual(1, completed2.Count, "Stream should be completed exactly once");

            sub1.Dispose();
            sub2.Dispose();
        }

        [Test]
        public void Dispose_MultipleSubscribersPerType_AllCompleted()
        {
            var (sub1, _, completed1) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();
            var (sub2, _, completed2) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();
            var (sub3, _, completed3) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();

            _eventBus.Dispose();

            Assert.AreEqual(1, completed1.Count, "First subscriber should be completed");
            Assert.AreEqual(1, completed2.Count, "Second subscriber should be completed");
            Assert.AreEqual(1, completed3.Count, "Third subscriber should be completed");

            sub1.Dispose();
            sub2.Dispose();
            sub3.Dispose();
        }

        #endregion

        #region Idempotent Disposal Tests

        [Test]
        public void Dispose_CalledTwice_SecondCallIsNoOp()
        {
            var (sub, values, completed) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();

            _eventBus.Dispose();
            Assert.AreEqual(1, completed.Count, "First dispose should complete stream");

            // Second dispose should be safe no-op
            Assert.DoesNotThrow(() => _eventBus.Dispose(), "Second dispose should not throw");
            Assert.AreEqual(1, completed.Count, "Completion should only happen once");

            sub.Dispose();
        }

        [Test]
        public void Dispose_CalledMultipleTimes_AlwaysSafe()
        {
            var (sub, _, completed) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();

            // Multiple disposal calls should all be safe
            for (int i = 0; i < 5; i++)
            {
                Assert.DoesNotThrow(() => _eventBus.Dispose(), $"Dispose call {i + 1} should be safe");
            }

            Assert.AreEqual(1, completed.Count, "Completion should only happen once regardless of multiple dispose calls");
            sub.Dispose();
        }

        #endregion

        #region Post-Disposal Behavior Tests

        [Test]
        public void Observe_AfterDispose_ThrowsObjectDisposedException()
        {
            _eventBus.Dispose();

            var ex = Assert.Throws<ObjectDisposedException>(() => _eventBus.Observe<TestEvent>());
            Assert.AreEqual(nameof(EventBus), ex.ObjectName);
        }

        [Test]
        public void Publish_AfterDispose_IgnoredSilently()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            _eventBus.Dispose();

            // Post-disposal publishes should be ignored without throwing
            Assert.DoesNotThrow(() => _eventBus.Publish(new TestEvent(42)));
            Assert.DoesNotThrow(() => _eventBus.Publish(new TestEvent(100)));

            Assert.AreEqual(0, received.Count, "No events should be delivered after disposal");
        }

        [Test]
        public void Observe_Publish_Dispose_Publish_Sequence()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Pre-disposal publish should work
            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(1, received.Count);

            _eventBus.Dispose();

            // Post-disposal publish should be ignored
            _eventBus.Publish(new TestEvent(2));
            Assert.AreEqual(1, received.Count, "Post-disposal events should not be delivered");
        }

        #endregion

        #region Cleanup and Resource Management Tests

        [Test]
        public void Dispose_ClearsInternalCollections()
        {
            // Create subjects for different event types
            using var sub1 = _eventBus.Observe<TestEvent>().Subscribe(_ => { });
            using var sub2 = _eventBus.Observe<AnotherEvent>().Subscribe(_ => { });

            _eventBus.Dispose();

            // After disposal, trying to observe should throw (indicating internal state is cleared)
            Assert.Throws<ObjectDisposedException>(() => _eventBus.Observe<TestEvent>());
            Assert.Throws<ObjectDisposedException>(() => _eventBus.Observe<AnotherEvent>());
        }

        [Test]
        public void Dispose_WithActiveSubscriptions_ProperCleanup()
        {
            var completed = new List<CompletionCounter>();
            var subscriptions = new List<IDisposable>();

            // Create multiple active subscriptions
            for (int i = 0; i < 10; i++)
            {
                var (sub, _, completedCounter) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();
                subscriptions.Add(sub);
                completed.Add(completedCounter);
            }

            // Publish some events
            _eventBus.Publish(new TestEvent(1));
            _eventBus.Publish(new TestEvent(2));

            // Dispose should complete all subscriptions
            _eventBus.Dispose();

            foreach (var counter in completed)
            {
                Assert.AreEqual(1, counter.Count, "Each subscription should be completed exactly once");
            }

            // Clean up subscriptions
            foreach (var sub in subscriptions)
            {
                sub.Dispose();
            }
        }

        #endregion

        #region Dispose During Operations Tests

        [Test]
        public void Dispose_DuringPublish_SafeCompletion()
        {
            var received = new List<TestEvent>();
            var completed = new CompletionCounter();
            bool disposeCalled = false;

            using var subscription = _eventBus.Observe<TestEvent>()
                .Subscribe(
                    onNext: evt =>
                    {
                        received.Add(evt);
                        if (!disposeCalled)
                        {
                            disposeCalled = true;
                            _eventBus.Dispose(); // Dispose during event handling
                        }
                    },
                    onCompleted: _ => Interlocked.Increment(ref completed.Count)
                );

            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received.Count, "Event should be received before disposal");
            Assert.AreEqual(1, completed.Count, "Stream should be completed after disposal");

            // Subsequent publishes should be ignored
            _eventBus.Publish(new TestEvent(100));
            Assert.AreEqual(1, received.Count, "No additional events should be received");
        }

        [Test]
        public void Dispose_WithUnsubscribedObservers_OnlyActiveStreamsCompleted()
        {
            var (sub1, _, completed1) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();
            var (sub2, _, completed2) = _eventBus.Observe<TestEvent>().SubscribeAndRecordWithCompletion();

            // Dispose one subscription manually
            sub1.Dispose();

            // Then dispose the EventBus
            _eventBus.Dispose();

            // Only the active subscription should be completed by bus disposal
            Assert.AreEqual(0, completed1.Count, "Manually disposed subscription should not be completed by bus disposal");
            Assert.AreEqual(1, completed2.Count, "Active subscription should be completed by bus disposal");

            sub2.Dispose();
        }

        #endregion

        #region Error During Disposal Tests

        [Test]
        public void Dispose_SubscriberThrowsInOnCompleted_DisposalContinues()
        {
            var completed1 = new CompletionCounter();
            var completed2 = new CompletionCounter();

            // First subscription throws in OnCompleted
            using var sub1 = _eventBus.Observe<TestEvent>()
                .Subscribe(
                    _ => { },
                    _ =>
                    {
                        Interlocked.Increment(ref completed1.Count);
                        throw new InvalidOperationException("OnCompleted exception");
                    }
                );

            // Second subscription should still be completed normally
            using var sub2 = _eventBus.Observe<AnotherEvent>()
                .Subscribe(
                    _ => { },
                    _ => Interlocked.Increment(ref completed2.Count)
                );

            // OnCompleted exception will be logged by Unity — declare it to avoid "Unhandled log message"
            LogAssert.Expect(LogType.Exception, new Regex("OnCompleted exception", RegexOptions.IgnoreCase));


            // Disposal should complete both despite exception in first
            Assert.DoesNotThrow(() => _eventBus.Dispose());

            Assert.AreEqual(1, completed1.Count, "First subscription should be completed despite exception");
            Assert.AreEqual(1, completed2.Count, "Second subscription should be completed normally");
        }

        #endregion

        #region Memory and Leak Prevention Tests

        [Test]
        public void Dispose_ReleasesSubjectReferences()
        {
            // Create subjects and then dispose
            using var sub1 = _eventBus.Observe<TestEvent>().Subscribe(_ => { });
            using var sub2 = _eventBus.Observe<AnotherEvent>().Subscribe(_ => { });

            _eventBus.Dispose();

            // After disposal, internal collections should be cleared
            // (We verify this indirectly by confirming ObjectDisposedException on further operations)
            Assert.Throws<ObjectDisposedException>(() => _eventBus.Observe<TestEvent>());
            Assert.DoesNotThrow(() => _eventBus.Publish(new TestEvent(1))); // publish is ignored silently

        }

        [Test]
        public void Dispose_DisposedFlagPreventsNewOperations()
        {
            _eventBus.Dispose();

            // All operations should respect the disposed flag
            Assert.Throws<ObjectDisposedException>(() => _eventBus.Observe<TestEvent>());
            Assert.DoesNotThrow(() => _eventBus.Publish(new TestEvent(1))); // Publish is silent

            // Multiple operations should all be blocked
            Assert.Throws<ObjectDisposedException>(() => _eventBus.Observe<AnotherEvent>());
            Assert.DoesNotThrow(() => _eventBus.Publish(new AnotherEvent("test")));
        }

        #endregion
    }
}
#endif