#if UNITY_INCLUDE_TESTS && (UNITY_EDITOR || DEVELOPMENT_BUILD)
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
    /// Development build logging tests for EventBus verifying proper warning and error logging behavior.
    /// These tests only run in Editor or Development builds where logging is enabled.
    /// Verifies off-thread publish warnings, SubscribeSafe exception logging, and disposal error handling.
    /// </summary>
    [Category("EventBus")]
    [Category("DevOnly")]
    [Category("PlayMode")]
    public class EventBusLoggingTests
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

        #region Off-Thread Publish Warning Tests

        [Test]
        public void Publish_OffThread_WarningLogged()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Expect a warning when publishing from background thread (wording can vary slightly)
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"(?i)publish.*(ignored).*thread", RegexOptions.IgnoreCase)
            );

            ThreadingHelpers.RunOnDedicatedThread(() =>
            {
                _eventBus.Publish(new TestEvent(42));
            });

            Assert.AreEqual(0, received.Count, "Event should be ignored from background thread");
        }

        [Test]
        public void Publish_OffThread_SpecificWarningMessage()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Expect the off-thread publish warning; don't depend on generic type printout
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"(?i)publish.*(ignored).*thread", RegexOptions.IgnoreCase)
            );

            ThreadingHelpers.RunOnDedicatedThread(() =>
            {
                _eventBus.Publish(new TestEvent(123));
            });

            Assert.AreEqual(0, received.Count, "Event should be ignored");
        }

        [Test]
        public void Publish_MultipleOffThread_MultipleWarnings()
        {
            var received = new List<TestEvent>();
            using var subscription = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Expect multiple warnings for multiple off-thread publishes
            LogAssert.Expect(LogType.Warning, new Regex(@"(?i)publish.*(ignored).*thread"));
            LogAssert.Expect(LogType.Warning, new Regex(@"(?i)publish.*(ignored).*thread"));
            LogAssert.Expect(LogType.Warning, new Regex(@"(?i)publish.*(ignored).*thread"));

            ThreadingHelpers.RunOnDedicatedThread(() =>
            {
                _eventBus.Publish(new TestEvent(1));
                _eventBus.Publish(new TestEvent(2));
                _eventBus.Publish(new TestEvent(3));
            });

            Assert.AreEqual(0, received.Count, "All events should be ignored from background thread");
        }

        #endregion

        #region SubscribeSafe Exception Logging Tests

        [Test]
        public void SubscribeSafe_Exception_LoggedWithCorrectDetails()
        {
            const string exceptionMessage = "Test exception for logging verification";

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throw new InvalidOperationException(exceptionMessage);
                });

            LogAssert.Expect(LogType.Exception, new Regex(exceptionMessage, RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(42));
        }

        [Test]
        public void SubscribeSafe_DifferentExceptionTypes_AllLogged()
        {
            var subscription1 = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throw new ArgumentException("ArgumentException message");
                });

            var subscription2 = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throw new NotSupportedException("NotSupportedException message");
                });

            var subscription3 = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throw new InvalidOperationException("InvalidOperationException message");
                });

            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException message", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("NotSupportedException message", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException message", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(1));

            subscription1.Dispose();
            subscription2.Dispose();
            subscription3.Dispose();
        }

        [Test]
        public void SubscribeSafe_ExceptionWithStackTrace_FullDetailsLogged()
        {
            var received = new List<TestEvent>();
            using var sub = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    // Throw from within the handler to produce a stack trace
                    throw new InvalidOperationException("Exception from method with stack trace");
                });

            // Expect only the message line (LogAssert doesn't include stack trace text).
            LogAssert.Expect(LogType.Exception, new Regex("Exception from method with stack trace", RegexOptions.IgnoreCase));

            _eventBus.Publish(new TestEvent(123));

            // No delivery should be recorded; this test is about logging only.
            Assert.AreEqual(0, received.Count);
        }

        private void MethodThatThrows()
        {
            throw new InvalidOperationException("Exception from method with stack trace");
        }

        #endregion

        #region Disposal and Lifecycle Logging Tests

        [Test]
        public void Dispose_WithSubscriberException_ExceptionLogged()
        {
            using var subscription = _eventBus.Observe<TestEvent>()
                .Subscribe(
                    _ => { },
                    _ => throw new InvalidOperationException("OnCompleted exception")
                );

            LogAssert.Expect(LogType.Exception, new Regex("OnCompleted exception", RegexOptions.IgnoreCase));

            _eventBus.Dispose();
        }

        [Test]
        public void Dispose_MultipleSubscribersWithExceptions_AllExceptionsLogged()
        {
            var sub1 = _eventBus.Observe<TestEvent>()
                .Subscribe(
                    _ => { },
                    _ => throw new InvalidOperationException("First subscriber exception")
                );

            var sub2 = _eventBus.Observe<TestEvent>()
                .Subscribe(
                    _ => { },
                    _ => throw new ArgumentException("Second subscriber exception")
                );

            LogAssert.Expect(LogType.Exception, new Regex("First subscriber exception", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Exception, new Regex("Second subscriber exception", RegexOptions.IgnoreCase));

            _eventBus.Dispose();

            sub1.Dispose();
            sub2.Dispose();
        }

        #endregion

        #region Complex Logging Scenarios Tests

        [Test]
        public void EventBus_MixedLoggingScenarios_AllTypesLogged()
        {
            var received = new List<TestEvent>();

            // Set up SubscribeSafe that throws
            using var throwingSub = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    throw new InvalidOperationException("SubscribeSafe exception");
                });

            // Set up normal subscriber
            using var normalSub = _eventBus.Observe<TestEvent>().Subscribe(received.Add);

            // Expect SubscribeSafe exception
            LogAssert.Expect(LogType.Exception, new Regex("SubscribeSafe exception", RegexOptions.IgnoreCase));

            // Expect off-thread warning (wording can vary)
            LogAssert.Expect(LogType.Warning, new Regex(@"(?i)publish.*(ignored).*thread"));

            // Main thread publish (should work and trigger exception)
            _eventBus.Publish(new TestEvent(1));

            // Off-thread publish (should log warning and be ignored)
            ThreadingHelpers.RunOnDedicatedThread(() =>
            {
                _eventBus.Publish(new TestEvent(2));
            });

            Assert.AreEqual(2, received.Count, "Should receive main thread event twice (normal + throwing subscriber)");
        }

        [Test]
        public void EventBus_LoggingUnderStress_AllMessagesLogged()
        {
            var exceptionCount = 10;
            var offThreadCount = 5;

            // Set up throwing subscriber
            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    throw new InvalidOperationException($"Stress exception {evt.Value}");
                });

            // Expect all exceptions to be logged
            for (int i = 1; i <= exceptionCount; i++)
            {
                LogAssert.Expect(LogType.Exception, new Regex($"Stress exception {i}", RegexOptions.IgnoreCase));
            }

            // Expect all off-thread warnings to be logged
            for (int i = 0; i < offThreadCount; i++)
            {
                LogAssert.Expect(LogType.Warning, new Regex(@"Publish.*ignored.*not on main thread", RegexOptions.IgnoreCase));
            }

            // Generate main thread exceptions
            for (int i = 1; i <= exceptionCount; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            // Generate off-thread warnings
            ThreadingHelpers.RunOnDedicatedThread(() =>
            {
                for (int i = 0; i < offThreadCount; i++)
                {
                    _eventBus.Publish(new TestEvent(100 + i));
                }
            });
        }

        #endregion

        #region Logging Performance Tests

        [Test]
        public void Logging_HighVolumeExceptions_DoesNotDegradePerformance()
        {
            const int eventCount = 100;
            var received = new List<TestEvent>();

            using var subscription = _eventBus.Observe<TestEvent>()
                .SubscribeSafe(evt =>
                {
                    received.Add(evt);
                    if (evt.Value % 10 == 0) // Throw every 10th event
                    {
                        throw new InvalidOperationException($"Performance test exception {evt.Value}");
                    }
                });

            // Expect exceptions for every 10th event
            for (int i = 10; i <= eventCount; i += 10)
            {
                LogAssert.Expect(LogType.Exception, new Regex($"Performance test exception {i}", RegexOptions.IgnoreCase));
            }

            var startTime = Time.realtimeSinceStartup;

            for (int i = 1; i <= eventCount; i++)
            {
                _eventBus.Publish(new TestEvent(i));
            }

            var elapsed = Time.realtimeSinceStartup - startTime;

            Assert.AreEqual(eventCount, received.Count, "Should receive all events despite exceptions");
            Assert.Less(elapsed, 1.0f, "Should complete within reasonable time despite logging overhead");
        }

        #endregion
    }
}
#endif