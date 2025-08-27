#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using R3;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Threading policy tests for EventBus verifying main-thread enforcement, off-thread publish behavior,
    /// and thread safety policies. These tests run in PlayMode to ensure proper Unity main thread detection.
    /// </summary>
    [Category("EventBus")]
    [Category("PlayMode")]
    public class EventBusThreadingTests
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

        #region Setup Sanity Tests

        [Test]
        public void Constructor_CapturesCorrectMainThread()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Main thread publish should work
            _eventBus.Publish(new TestEvent(42));
            Assert.AreEqual(1, received.Count, "Main thread publish should succeed");
            Assert.AreEqual(42, received[0].Value);

            // Background thread publish should be ignored
            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                _eventBus.Publish(new TestEvent(100));
            });

            Assert.AreEqual(1, received.Count, "Background thread publish should be ignored");
        }

        #endregion

        #region Main-Thread Policy Tests

        [Test]
        public void Publish_FromMainThread_EventDelivered()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Standard main-thread publish scenario
            _eventBus.Publish(new TestEvent(42));

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(42, received[0].Value);
        }

        [Test]
        public void Publish_FromBackgroundThread_EventIgnored()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Publish from background thread
            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                _eventBus.Publish(new TestEvent(42));
            });

            Assert.AreEqual(0, received.Count, "Background thread publish should be ignored");
        }

        #endregion

        #region Off-Thread Warning Tests (Development Only)

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        [Category("DevOnly")]
        public void Publish_OffThread_WarningLogged()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Expect a warning when publishing from background thread
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Publish.*ignored.*not on main thread"));

            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                _eventBus.Publish(new TestEvent(42));
            });

            Assert.AreEqual(0, received.Count);
        }
#endif

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        [Test]
        [Category("Release")]
        public void Publish_OffThread_NoWarningInRelease()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Should not log any warnings in release build
            LogAssert.NoUnexpectedReceived();

            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                _eventBus.Publish(new TestEvent(42));
            });

            Assert.AreEqual(0, received.Count, "Event should still be ignored in release");
        }
#endif

        #endregion

        #region Concurrent Access Policy Tests

        [Test]
        public void Threading_MainThreadOnly_PolicyEnforced()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Main thread operations should succeed
            _eventBus.Publish(new TestEvent(1));
            Assert.AreEqual(1, received.Count);

            // Off-thread operations should be ignored
            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                _eventBus.Publish(new TestEvent(2));
                _eventBus.Publish(new TestEvent(3));
            });

            // Should still only have the main thread event
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(1, received[0].Value);

            // Additional main thread publish should work
            _eventBus.Publish(new TestEvent(4));
            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(4, received[1].Value);
        }

        [Test]
        public void Observe_FromMainThread_WorksCorrectly()
        {
            // Creating observables from main thread should work
            var observable = _eventBus.Observe<TestEvent>();
            Assert.IsNotNull(observable);

            var received = new List<TestEvent>();
            using var subscription = observable.Subscribe(received.Add);

            _eventBus.Publish(new TestEvent(42));
            Assert.AreEqual(1, received.Count);
        }

        [Test]
        public void Observe_FromBackgroundThread_WorksButPublishStillIgnored()
        {
            Observable<TestEvent> observable = null;
            List<TestEvent> received = null;
            IDisposable subscription = null;
            var ready = new ManualResetEvent(false);

            // Creating observables from background thread (implementation detail - may work)
            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                observable = _eventBus.Observe<TestEvent>();
                received = new List<TestEvent>();
                subscription = observable.Subscribe(received.Add);
                ready.Set();
            });

            // Ensure subscription is established before publishing
            ready.WaitOne();

            // Publish from main thread should work
            _eventBus.Publish(new TestEvent(42));

            // Even though observed from background thread, main thread publish should work
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(42, received[0].Value);

            // Background publish should still be ignored
            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                _eventBus.Publish(new TestEvent(100));
            });

            Assert.AreEqual(1, received.Count, "Background publish should be ignored regardless of where observer was created");

            subscription?.Dispose();
        }

        #endregion

        #region Multiple Thread Coordination Tests

        [Test]
        public void Publish_MultipleBackgroundThreads_AllIgnored()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            var barrier = new ThreadBarrier();
            const int threadCount = 5;
            var tasks = new System.Threading.Tasks.Task[threadCount];

            // Start multiple background threads that will all try to publish
            for (int i = 0; i < threadCount; i++)
            {
                int threadId = i;
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                {
                    barrier.Wait(); // Wait for all threads to be ready
                    _eventBus.Publish(new TestEvent(threadId));
                });
            }

            // Let all threads proceed simultaneously
            barrier.Signal();

            // Wait for all threads to complete
            System.Threading.Tasks.Task.WaitAll(tasks);

            // None of the background thread publishes should have succeeded
            Assert.AreEqual(0, received.Count, "All background thread publishes should be ignored");

            // Main thread publish should still work
            _eventBus.Publish(new TestEvent(999));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(999, received[0].Value);

            barrier.Dispose();
        }

        [Test]
        public void Dispose_FromBackgroundThread_SafeBehavior()
        {
            var completed = new ManualResetEvent(false);
            using var subscription = _eventBus.Observe<TestEvent>()
                .Subscribe(_ => { }, _ => completed.Set());

            // Dispose from background thread
            ThreadingHelpers.RunOnBackgroundThread(() =>
            {
                _eventBus.Dispose();
            });

            Assert.IsTrue(completed.WaitOne(5000), "Disposal from background thread should complete streams");


            // Further publishes should be ignored (disposed)
            Assert.DoesNotThrow(() => _eventBus.Publish(new TestEvent(42)));
        }

        #endregion
    }
}
#endif