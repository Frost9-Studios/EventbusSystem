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
    /// Edge case tests for EventBus UniTask extensions covering race conditions, disposal scenarios,
    /// concurrent cancellation, subscription cleanup verification, and complex async interaction patterns.
    /// These tests verify robust behavior under challenging timing and lifecycle scenarios.
    /// </summary>
    [Category("EventBus")]
    [Category("EditMode")]
    public class UniTaskEdgeCasesTests
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

        public readonly struct RaceEvent
        {
            public readonly string Id;
            public readonly int Sequence;
            public RaceEvent(string id, int sequence) { Id = id; Sequence = sequence; }
        }

        #region Race Condition Tests

        [UnityTest]
        public IEnumerator Next_Race_EventAndCancel_SettlesDeterministically()
        {
            using var cts = new CancellationTokenSource();
            var nextTask = _eventBus.Next<TestEvent>(ct: cts.Token);

            bool completed = false;
            bool cancelled = false;
            TestEvent result = default;
            Exception caughtException = null;

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
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            }

            RunAsync().Forget();

            // Create a race condition: publish event and cancel simultaneously
            _eventBus.Publish(new TestEvent(42));
            cts.Cancel();

            // Wait for either completion or cancellation
            yield return new WaitUntil(() => completed || cancelled || caughtException != null);

            // Should settle deterministically - either completed or cancelled, not both
            Assert.IsTrue(completed ^ cancelled, "Should be either completed XOR cancelled, not both or neither");

            if (completed)
            {
                Assert.AreEqual(42, result.Value, "If completed, should have correct result");
                Assert.IsFalse(cancelled, "Should not be cancelled if completed");
            }
            else if (cancelled)
            {
                Assert.IsFalse(completed, "Should not be completed if cancelled");
            }

            Assert.IsNull(caughtException, "Should not throw unexpected exceptions");
        }

        [UnityTest]
        public IEnumerator Next_Race_EventAndDispose_BehaviorDocumented()
        {
            var nextTask = _eventBus.Next<TestEvent>();

            bool completed = false;
            bool faulted = false;
            TestEvent result = default;
            Exception caughtException = null;

            async UniTaskVoid RunAsync()
            {
                try
                {
                    result = await nextTask;
                    completed = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                    faulted = true;
                }
            }

            RunAsync().Forget();

            // Create race: publish and dispose simultaneously
            _eventBus.Publish(new TestEvent(100));
            _eventBus.Dispose();

            // Wait for resolution
            yield return new WaitUntil(() => completed || faulted);

            // Document expected behavior: should either complete successfully or fault
            Assert.IsTrue(completed ^ faulted, "Should either complete OR fault, not both");

            if (completed)
            {
                Assert.AreEqual(100, result.Value, "If completed, should have correct value");
            }
            else if (faulted)
            {
                Assert.IsNotNull(caughtException, "If faulted, should have exception");
            }
        }

        [UnityTest]
        public IEnumerator Next_MultipleConcurrentCancellations_NoCrossContamination()
        {
            using var cts1 = new CancellationTokenSource();
            using var cts2 = new CancellationTokenSource();
            using var cts3 = new CancellationTokenSource();

            var task1 = _eventBus.Next<RaceEvent>(e => e.Id == "A", cts1.Token);
            var task2 = _eventBus.Next<RaceEvent>(e => e.Id == "B", cts2.Token);
            var task3 = _eventBus.Next<RaceEvent>(e => e.Id == "C", cts3.Token);

            bool completed1 = false, cancelled1 = false;
            bool completed2 = false, cancelled2 = false;
            bool completed3 = false, cancelled3 = false;

            async UniTaskVoid Run1()
            {
                try { await task1; completed1 = true; }
                catch (OperationCanceledException) { cancelled1 = true; }
            }

            async UniTaskVoid Run2()
            {
                try { await task2; completed2 = true; }
                catch (OperationCanceledException) { cancelled2 = true; }
            }

            async UniTaskVoid Run3()
            {
                try { await task3; completed3 = true; }
                catch (OperationCanceledException) { cancelled3 = true; }
            }

            Run1().Forget();
            Run2().Forget();
            Run3().Forget();

            // Cancel task 1, complete task 2, cancel task 3
            cts1.Cancel();
            _eventBus.Publish(new RaceEvent("B", 1));
            cts3.Cancel();

            yield return new WaitUntil(() => (completed1 || cancelled1) &&
                                            (completed2 || cancelled2) &&
                                            (completed3 || cancelled3));

            // Verify no cross-contamination
            Assert.IsTrue(cancelled1 && !completed1, "Task 1 should be cancelled only");
            Assert.IsTrue(completed2 && !cancelled2, "Task 2 should be completed only");
            Assert.IsTrue(cancelled3 && !completed3, "Task 3 should be cancelled only");
        }

        #endregion

        #region Subscription Cleanup Verification Tests

        [UnityTest]
        public IEnumerator Next_MassiveParallelWaiters_AllCleanupProperly()
        {
            const int waiterCount = 50;
            var tasks = new UniTask<TestEvent>[waiterCount];
            var completions = new bool[waiterCount];
            var results = new TestEvent[waiterCount];

            // Create many parallel Next waiters
            for (int i = 0; i < waiterCount; i++)
            {
                int index = i;
                tasks[i] = _eventBus.Next<TestEvent>();

                async UniTaskVoid RunAsync()
                {
                    results[index] = await tasks[index];
                    completions[index] = true;
                }

                RunAsync().Forget();
            }

            // Verify none completed yet
            yield return null;
            Assert.IsTrue(System.Array.TrueForAll(completions, c => !c), "No tasks should complete initially");

            // Publish single event - should complete all waiters
            _eventBus.Publish(new TestEvent(123));

            // Wait for all to complete
            yield return new WaitUntil(() => System.Array.TrueForAll(completions, c => c));

            // Verify all received same event
            for (int i = 0; i < waiterCount; i++)
            {
                Assert.IsTrue(completions[i], $"Waiter {i} should be completed");
                Assert.AreEqual(123, results[i].Value, $"Waiter {i} should have correct result");
            }

            // Verify cleanup: publish more events and ensure no ghost listeners
            var deliveryCount = 0;
            using var probe = _eventBus.Observe<TestEvent>().Subscribe(_ => deliveryCount++);

            for (int i = 1; i <= 10; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            yield return null;

            // Only probe should receive events (no lingering Next subscriptions)
            Assert.AreEqual(10, deliveryCount, "Only probe should receive subsequent events");
        }

        [UnityTest]
        public IEnumerator Next_CancelledBeforeEvent_NoMemoryLeak()
        {
            const int iterations = 100;
            using var cts = new CancellationTokenSource();
            var cancelledCount = 0;

            // Create and immediately cancel many Next operations
            for (int i = 0; i < iterations; i++)
            {
                var task = _eventBus.Next<TestEvent>(ct: cts.Token);

                async UniTaskVoid RunAsync()
                {
                    try { await task; }
                    catch (OperationCanceledException) { /* Expected */ }
                    finally { Interlocked.Increment(ref cancelledCount); }
                }

                RunAsync().Forget();
            }

            // Cancel all at once
            cts.Cancel();

            // Wait until all cancellations have actually observed the token
            yield return new WaitUntil(() => Volatile.Read(ref cancelledCount) == iterations);

            // Verify no ghost listeners remain
            var deliveryCount = 0;
            using var probe = _eventBus.Observe<TestEvent>().Subscribe(_ => deliveryCount++);

            _eventBus.Publish(new TestEvent(999));
            yield return null;

            Assert.AreEqual(1, deliveryCount, "Should have exactly one delivery to probe only");
        }

        #endregion

        #region Complex Predicate and Filtering Edge Cases

        [UnityTest]
        public IEnumerator Next_PredicateThrows_FaultsCorrectly()
        {
            var nextTask = _eventBus.Next<TestEvent>(evt =>
            {
                if (evt.Value == 42)
                    throw new InvalidOperationException("Predicate exception");
                return evt.Value > 10;
            });

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

            // Publish event that triggers predicate exception
            _eventBus.Publish(new TestEvent(42));

            yield return new WaitUntil(() => completed || faulted);

            Assert.IsTrue(faulted, "Task should fault when predicate throws");
            Assert.IsFalse(completed, "Task should not complete when predicate throws");
            Assert.IsNotNull(caughtException, "Should have caught the predicate exception");
            Assert.IsTrue(caughtException.Message.Contains("Predicate exception"), "Should contain predicate exception message");
        }

        [UnityTest]
        public IEnumerator Next_PredicateReturnsFalseForAllEvents_NeverCompletes()
        {
            var nextTask = _eventBus.Next<TestEvent>(evt => false); // Never matches
            bool completed = false;

            async UniTaskVoid RunAsync()
            {

                try
                {
                    await nextTask;
                    completed = true;
                }
                catch (Exception)
                {
                    // If the bus is disposed during/after the test, Next<T> may fault.
                    // Swallow to avoid unobserved exceptions leaking into other tests.
                }
            }

            RunAsync().Forget();

            // Publish many events
            for (int i = 1; i <= 20; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            // Let a few frames elapse; task should still be pending
            yield return null;
            yield return null;

            Assert.IsFalse(completed, "Task should never complete when predicate always returns false");
        }

        #endregion

        #region Disposal During Next Operation Tests

        [UnityTest]
        public IEnumerator Next_BusDisposedDuringPredicateEvaluation_SafeHandling()
        {
            var nextTask = _eventBus.Next<TestEvent>(evt =>
            {
                // Dispose bus during predicate evaluation
                _eventBus.Dispose();
                return evt.Value > 5;
            });

            bool completed = false;
            bool faulted = false;

            async UniTaskVoid RunAsync()
            {
                try
                {
                    await nextTask;
                    completed = true;
                }
                catch (Exception)
                {
                    faulted = true;
                }
            }

            RunAsync().Forget();

            _eventBus.Publish(new TestEvent(10));

            yield return new WaitUntil(() => completed || faulted);

            // Should either complete (if event was processed before disposal) or fault
            Assert.IsTrue(completed ^ faulted, "Should either complete or fault, not both");
        }

        [UnityTest]
        public IEnumerator Next_DisposeDuringEventPublish_ConsistentBehavior()
        {
            var completionCount = 0;
            var faultCount = 0;
            const int taskCount = 10;

            // Create multiple Next waiters
            for (int i = 0; i < taskCount; i++)
            {
                var task = _eventBus.Next<TestEvent>();

                async UniTaskVoid RunAsync()
                {
                    try
                    {
                        await task;
                        Interlocked.Increment(ref completionCount);
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref faultCount);
                    }
                }

                RunAsync().Forget();
            }

            // Publish and immediately dispose
            _eventBus.Publish(new TestEvent(50));
            _eventBus.Dispose();

            yield return new WaitUntil(() => completionCount + faultCount == taskCount);

            // All tasks should resolve one way or another
            Assert.AreEqual(taskCount, completionCount + faultCount, "All tasks should be resolved");

            // Either all complete or all fault (consistent behavior)
            Assert.IsTrue(completionCount == taskCount || faultCount == taskCount,
                "Should have consistent behavior across all tasks");
        }

        #endregion

        #region Stress and Performance Edge Cases

        [UnityTest]
        public IEnumerator Next_HighFrequencyPublishWithRareMatch_PerformsWell()
        {
            var nextTask = _eventBus.Next<TestEvent>(evt => evt.Value == 9999); // Very rare match
            bool completed = false;
            TestEvent result = default;

            async UniTaskVoid RunAsync()
            {
                result = await nextTask;
                completed = true;
            }

            RunAsync().Forget();

            // Publish many non-matching events rapidly
            for (int i = 1; i <= 1000; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            yield return null;
            Assert.IsFalse(completed, "Should not complete before matching event");

            // Publish the matching event
            _eventBus.Publish(new TestEvent(9999));

            yield return new WaitUntil(() => completed);

            Assert.IsTrue(completed, "Should complete on matching event");
            Assert.AreEqual(9999, result.Value, "Should have correct result");
        }

        [UnityTest]
        public IEnumerator Next_ChainedAsyncOperations_WorkCorrectly()
        {
            var step1Complete = false;
            var step2Complete = false;
            var finalResult = 0;

            async UniTaskVoid ChainedOperations()
            {
                // Step 1: Wait for first event
                var event1 = await _eventBus.Next<TestEvent>(e => e.Value == 100);
                step1Complete = true;

                // Step 2: Wait for second event  
                var event2 = await _eventBus.Next<TestEvent>(e => e.Value == 200);
                step2Complete = true;

                finalResult = event1.Value + event2.Value;
            }

            ChainedOperations().Forget();

            // Publish first event
            _eventBus.Publish(new TestEvent(100));
            yield return new WaitUntil(() => step1Complete);

            Assert.IsTrue(step1Complete, "Step 1 should complete");
            Assert.IsFalse(step2Complete, "Step 2 should not complete yet");

            // Publish second event
            _eventBus.Publish(new TestEvent(200));
            yield return new WaitUntil(() => step2Complete);

            Assert.IsTrue(step2Complete, "Step 2 should complete");
            Assert.AreEqual(300, finalResult, "Final result should be sum of both events");
        }

        #endregion

        #region Timeout and Deadline Edge Cases

        [UnityTest]
        public IEnumerator Next_WithTimeoutCancellation_CancelsCorrectly()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); // 100ms timeout
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

            // Don't publish any events - should timeout
            yield return new WaitUntil(() => cancelled || completed); // Wait longer than timeout

            Assert.IsTrue(cancelled, "Should be cancelled due to timeout");
            Assert.IsFalse(completed, "Should not complete due to timeout");
        }

        #endregion
    }
}
#endif