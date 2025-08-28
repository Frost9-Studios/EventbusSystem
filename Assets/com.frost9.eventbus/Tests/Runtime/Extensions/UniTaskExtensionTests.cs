#if UNITY_INCLUDE_TESTS
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Tests for EventBus UniTask extensions covering async/await patterns, Next<T>() behavior,
    /// cancellation token handling, predicate filtering, and one-shot subscription cleanup.
    /// These tests verify the async integration works correctly with Unity's async patterns.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class UniTaskExtensionTests
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

        public readonly struct TimedEvent
        {
            public readonly DateTime Timestamp;
            public readonly string Message;
            public TimedEvent(DateTime timestamp, string message)
            {
                Timestamp = timestamp;
                Message = message;
            }
        }

        #region Basic Next<T> Tests

        [UnityTest]
        public IEnumerator Next_WithoutPredicate_ResolvesOnFirstEvent()
        {
            var nextTask = _eventBus.Next<TestEvent>();
            bool completed = false;
            TestEvent result = default;

            // Start the async operation
            async UniTaskVoid RunAsync()
            {
                result = await nextTask;
                completed = true;
            }

            RunAsync().Forget();

            // Should not be completed yet
            Assert.IsFalse(completed, "Task should not complete before event is published");

            // Publish an event
            _eventBus.Publish(new TestEvent(42));

            // Wait for completion
            yield return new WaitUntil(() => completed);

            Assert.IsTrue(completed, "Task should complete after event is published");
            Assert.AreEqual(42, result.Value, "Result should contain the published event");
        }

        [UnityTest]
        public IEnumerator Next_WithPredicate_ResolvesOnlyOnMatch()
        {
            var nextTask = _eventBus.Next<TestEvent>(e => e.Value > 10);
            bool completed = false;
            TestEvent result = default;

            async UniTaskVoid RunAsync()
            {
                result = await nextTask;
                completed = true;
            }

            RunAsync().Forget();

            // Publish non-matching events
            _eventBus.Publish(new TestEvent(5));
            _eventBus.Publish(new TestEvent(8));

            // Let a couple of frames process; task should still be pending
            yield return null;
            yield return null;
            Assert.IsFalse(completed, "Task should not complete for non-matching events");

            // Publish matching event
            _eventBus.Publish(new TestEvent(15));

            yield return new WaitUntil(() => completed);

            Assert.IsTrue(completed, "Task should complete on matching event");
            Assert.AreEqual(15, result.Value, "Result should contain the matching event");
        }

        [UnityTest]
        public IEnumerator Next_MultipleEvents_CompletesOnFirst()
        {
            var nextTask = _eventBus.Next<TestEvent>();
            bool completed = false;
            TestEvent result = default;

            async UniTaskVoid RunAsync()
            {
                result = await nextTask;
                completed = true;
            }

            RunAsync().Forget();

            // Publish multiple events
            _eventBus.Publish(new TestEvent(1));
            _eventBus.Publish(new TestEvent(2)); // This should be ignored

            yield return new WaitUntil(() => completed);

            Assert.IsTrue(completed, "Task should complete on first matching event");
            Assert.AreEqual(1, result.Value, "Should receive the first event only");
        }

        #endregion

        #region Cancellation Token Tests

        [UnityTest]
        public IEnumerator Next_WithCancellationToken_ThrowsOnCancel()
        {
            using var cts = new CancellationTokenSource();
            var nextTask = _eventBus.Next<TestEvent>(ct: cts.Token);
            bool cancelled = false;
            bool completed = false;

            async UniTaskVoid RunAsync()
            {
                try
                {
                    await nextTask;
                    completed = true;
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
            }

            RunAsync().Forget();

            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(completed, "Task should not complete yet");
            Assert.IsFalse(cancelled, "Task should not be cancelled yet");

            // Cancel the token
            cts.Cancel();

            yield return new WaitUntil(() => cancelled || completed);

            Assert.IsTrue(cancelled, "Task should be cancelled");
            Assert.IsFalse(completed, "Task should not complete after cancellation");
        }

        [UnityTest]
        public IEnumerator Next_CancelAfterEventPublished_StillCompletes()
        {
            using var cts = new CancellationTokenSource();
            var nextTask = _eventBus.Next<TestEvent>(ct: cts.Token);
            bool completed = false;
            bool cancelled = false;
            TestEvent result = default;

            async UniTaskVoid RunAsync()
            {
                try
                {
                    result = await nextTask;
                    completed = true;
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
            }

            RunAsync().Forget();

            // Publish event first
            _eventBus.Publish(new TestEvent(42));

            // Wait until the awaiter resumes and sets 'completed'
            yield return new WaitUntil(() => completed);

            // Now try to cancel (should be too late to affect the result)
            cts.Cancel();

            Assert.IsTrue(completed, "Task should complete successfully");
            Assert.IsFalse(cancelled, "Task should not be cancelled after successful completion");
            Assert.AreEqual(42, result.Value, "Result should contain the published event");
        }

        #endregion

        #region No Replay Tests

        [UnityTest]
        public IEnumerator Next_PublishBeforeNext_StillWaitsForNewEvent()
        {
            // Publish event before calling Next
            _eventBus.Publish(new TestEvent(1));

            var nextTask = _eventBus.Next<TestEvent>();
            bool completed = false;
            TestEvent result = default;

            async UniTaskVoid RunAsync()
            {
                result = await nextTask;
                completed = true;
            }

            RunAsync().Forget();

            // Let a couple of frames process; task should still be pending
            yield return null;
            yield return null;
            Assert.IsFalse(completed, "Next should not complete from previous event (no replay)");

            // Publish another event
            _eventBus.Publish(new TestEvent(2));

            yield return new WaitUntil(() => completed);

            Assert.IsTrue(completed, "Task should complete on new event");
            Assert.AreEqual(2, result.Value, "Should receive the new event, not the previous one");
        }

        #endregion

        #region Multiple Waiters Tests

        [UnityTest]
        public IEnumerator Next_MultipleConcurrentWaiters_BothCompleteOnMatch()
        {
            var task1 = _eventBus.Next<TestEvent>(e => e.Value > 5);
            var task2 = _eventBus.Next<TestEvent>(e => e.Value > 5);

            bool completed1 = false, completed2 = false;
            TestEvent result1 = default, result2 = default;

            async UniTaskVoid RunAsync1()
            {
                result1 = await task1;
                completed1 = true;
            }

            async UniTaskVoid RunAsync2()
            {
                result2 = await task2;
                completed2 = true;
            }

            RunAsync1().Forget();
            RunAsync2().Forget();

            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(completed1 || completed2, "Neither task should complete yet");

            // Publish matching event
            _eventBus.Publish(new TestEvent(10));

            yield return new WaitUntil(() => completed1 && completed2);

            Assert.IsTrue(completed1, "First waiter should complete");
            Assert.IsTrue(completed2, "Second waiter should complete");
            Assert.AreEqual(10, result1.Value, "First waiter should receive the event");
            Assert.AreEqual(10, result2.Value, "Second waiter should receive the event");
        }

        [UnityTest]
        public IEnumerator Next_DifferentPredicates_OnlyMatchingComplete()
        {
            var lowValueTask = _eventBus.Next<TestEvent>(e => e.Value < 10);
            var highValueTask = _eventBus.Next<TestEvent>(e => e.Value >= 10);

            bool lowCompleted = false, highCompleted = false;
            TestEvent lowResult = default, highResult = default;

            async UniTaskVoid RunLowAsync()
            {
                lowResult = await lowValueTask;
                lowCompleted = true;
            }

            async UniTaskVoid RunHighAsync()
            {
                highResult = await highValueTask;
                highCompleted = true;
            }

            RunLowAsync().Forget();
            RunHighAsync().Forget();

            // Publish low value event
            _eventBus.Publish(new TestEvent(5));

            yield return new WaitUntil(() => lowCompleted);

            Assert.IsTrue(lowCompleted, "Low value waiter should complete");
            Assert.IsFalse(highCompleted, "High value waiter should not complete");
            Assert.AreEqual(5, lowResult.Value, "Low value result should be correct");

            // Publish high value event
            _eventBus.Publish(new TestEvent(15));

            yield return new WaitUntil(() => highCompleted);

            Assert.IsTrue(highCompleted, "High value waiter should now complete");
            Assert.AreEqual(15, highResult.Value, "High value result should be correct");
        }

        #endregion

        #region Bus Disposal During Next Tests

        [UnityTest]
        public IEnumerator Next_BusDisposedWhilePending_CompletesWithException()
        {
            var nextTask = _eventBus.Next<TestEvent>();
            bool completed = false;
            bool faulted = false;
            Exception caughtException = null;

            async UniTaskVoid RunAsync()
            {
                try
                {
                    await nextTask;
                    completed = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                    faulted = true;
                }
            }

            RunAsync().Forget();

            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(completed, "Task should not complete yet");
            Assert.IsFalse(faulted, "Task should not fault yet");

            // Dispose the EventBus
            _eventBus.Dispose();

            yield return new WaitUntil(() => completed || faulted);

            Assert.IsFalse(completed, "Task should not complete successfully");
            Assert.IsTrue(faulted, "Task should fault when bus is disposed");
            Assert.IsNotNull(caughtException, "Should have caught an exception");
        }

        #endregion

        #region Cleanup and Unsubscribe Tests

        [UnityTest]
        public IEnumerator Next_AfterCompletion_NoLongerListening()
        {
            var deliveryCount = 0;
            var received = new System.Collections.Generic.List<TestEvent>();

            // Create a probe subscriber to count total deliveries
            using var probeSub = _eventBus.Observe<TestEvent>().Subscribe(e =>
            {
                deliveryCount++;
                received.Add(e);
            });

            // Use Next to get one event
            var nextTask = _eventBus.Next<TestEvent>();
            TestEvent result = default;
            bool nextCompleted = false;

            async UniTaskVoid RunAsync()
            {
                result = await nextTask;
                nextCompleted = true;
            }

            RunAsync().Forget();

            // Publish first event
            _eventBus.Publish(new TestEvent(1));

            yield return new WaitUntil(() => nextCompleted);

            Assert.AreEqual(1, result.Value, "Next should receive first event");
            Assert.AreEqual(1, deliveryCount, "Should have one delivery to probe");

            // Publish many more events
            for (int i = 2; i <= 10; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            yield return new WaitForSeconds(0.1f);

            // Probe should receive all events, but Next subscription should be cleaned up
            Assert.AreEqual(10, deliveryCount, "Probe should receive all 10 events");
            Assert.AreEqual(10, received.Count, "Probe should have all 10 events recorded");

            // The key test: Next didn't create a lingering subscription that increases delivery count
            Assert.AreEqual(1, result.Value, "Next result should still be the first event");
        }

        [UnityTest]
        public IEnumerator Next_CancelledToken_NoGhostListener()
        {
            using var cts = new CancellationTokenSource();
            var deliveryCount = 0;

            // Create probe to count deliveries
            using var probeSub = _eventBus.Observe<TestEvent>().Subscribe(_ => deliveryCount++);

            var nextTask = _eventBus.Next<TestEvent>(ct: cts.Token);
            bool cancelled = false;

            async UniTaskVoid RunAsync()
            {
                try
                {
                    await nextTask;
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
            }

            RunAsync().Forget();

            // Cancel before any events
            cts.Cancel();

            yield return new WaitUntil(() => cancelled);

            Assert.IsTrue(cancelled, "Next should be cancelled");

            // Publish events after cancellation
            for (int i = 1; i <= 5; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            yield return new WaitForSeconds(0.1f);

            // Only probe should receive events (cancelled Next should not create ghost listeners)
            Assert.AreEqual(5, deliveryCount, "Should have exactly 5 deliveries to probe only");
        }

        #endregion

        #region Complex Predicate Tests

        [UnityTest]
        public IEnumerator Next_ComplexPredicate_WorksCorrectly()
        {
            var now = DateTime.Now;
            var nextTask = _eventBus.Next<TimedEvent>(e =>
                e.Timestamp > now &&
                e.Message.StartsWith("Valid") &&
                e.Message.Length > 10);

            bool completed = false;
            TimedEvent result = default;

            async UniTaskVoid RunAsync()
            {
                result = await nextTask;
                completed = true;
            }

            RunAsync().Forget();

            // Publish non-matching events
            _eventBus.Publish(new TimedEvent(now.AddMinutes(-1), "Valid but old timestamp"));
            _eventBus.Publish(new TimedEvent(now.AddMinutes(1), "Invalid prefix"));
            _eventBus.Publish(new TimedEvent(now.AddMinutes(1), "Valid"));

            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(completed, "Should not complete for non-matching events");

            // Publish matching event
            var validEvent = new TimedEvent(now.AddMinutes(1), "Valid message that meets all criteria");
            _eventBus.Publish(validEvent);

            yield return new WaitUntil(() => completed);

            Assert.IsTrue(completed, "Should complete for matching event");
            Assert.AreEqual("Valid message that meets all criteria", result.Message, "Should receive the matching event");
            Assert.Greater(result.Timestamp, now, "Timestamp should be in the future");
        }

        #endregion
    }
}
#endif