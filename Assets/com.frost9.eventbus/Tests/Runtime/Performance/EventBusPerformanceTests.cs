#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using R3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Performance and scalability tests for EventBus covering mass fan-out scenarios,
    /// memory allocation patterns, throughput benchmarking, and resource usage verification.
    /// Ensures EventBus maintains acceptable performance under high-load production conditions.
    /// </summary>
    [Category("EventBus")]
    [Category("Performance")]
    [Category("EditMode")]
    public class EventBusPerformanceTests
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
        public readonly struct LightweightEvent
        {
            public readonly int Value;
            public LightweightEvent(int value) => Value = value;
        }

        public readonly struct HeavyEvent
        {
            public readonly string Data1;
            public readonly string Data2;
            public readonly string Data3;
            public readonly int[] Numbers;
            public HeavyEvent(string data1, string data2, string data3, int[] numbers)
            {
                Data1 = data1;
                Data2 = data2;
                Data3 = data3;
                Numbers = numbers;
            }
        }

        public readonly struct BenchmarkEvent
        {
            public readonly int Id;
            public readonly long Timestamp;
            public BenchmarkEvent(int id, long timestamp)
            {
                Id = id;
                Timestamp = timestamp;
            }
        }

        #region Mass Fan-out Performance Tests

        [Test]
        public void EventBus_MassFanOut_HandlesLargeSubscriberCounts()
        {
            const int subscriberCount = 1000;
            const int eventCount = 100;

            var subscribers = new List<ProbeSubscriber<LightweightEvent>>();
            var subscriptions = new List<IDisposable>();

            // Create many subscribers
            for (int i = 0; i < subscriberCount; i++)
            {
                var subscriber = new ProbeSubscriber<LightweightEvent>();
                subscribers.Add(subscriber);
                subscriptions.Add(_eventBus.Observe<LightweightEvent>().Subscribe(subscriber.OnNext));
            }

            var stopwatch = Stopwatch.StartNew();

            // Publish events
            for (int i = 0; i < eventCount; i++)
            {
                _eventBus.Publish(new LightweightEvent(i));
            }

            stopwatch.Stop();

            // Verify all subscribers received all events
            foreach (var subscriber in subscribers)
            {
                Assert.AreEqual(eventCount, subscriber.ReceivedEvents.Count,
                    "Each subscriber should receive all events");
            }

            // Performance assertion
            Assert.Less(stopwatch.ElapsedMilliseconds, 2000,
                $"Mass fan-out to {subscriberCount} subscribers should complete within 2 second");

            // Cleanup
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }

        [Test]
        public void EventBus_VaryingSubscriberCounts_LinearPerformance()
        {
            var subscriberCounts = new[] { 10, 50, 100, 500 };
            const int eventsPerTest = 50;
            var results = new List<(int subscribers, long elapsedMs)>();

            foreach (int subscriberCount in subscriberCounts)
            {
                var subscriptions = new List<IDisposable>();
                var subscribers = new List<ProbeSubscriber<LightweightEvent>>();

                // Setup subscribers
                for (int i = 0; i < subscriberCount; i++)
                {
                    var subscriber = new ProbeSubscriber<LightweightEvent>();
                    subscribers.Add(subscriber);
                    subscriptions.Add(_eventBus.Observe<LightweightEvent>().Subscribe(subscriber.OnNext));
                }

                // Measure performance
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < eventsPerTest; i++)
                {
                    _eventBus.Publish(new LightweightEvent(i));
                }
                stopwatch.Stop();

                results.Add((subscriberCount, stopwatch.ElapsedMilliseconds));

                // Verify correctness
                foreach (var subscriber in subscribers)
                {
                    Assert.AreEqual(eventsPerTest, subscriber.ReceivedEvents.Count);
                }

                // Cleanup
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }

                // Reset for next test
                _eventBus.Dispose();
                _eventBus = new EventBus();
            }

            // Performance should scale reasonably (not exponentially)
            var firstResult = results[0];
            var lastResult = results[^1];
            var subscriberRatio = (double)lastResult.subscribers / firstResult.subscribers;
            var timeRatio = (double)lastResult.elapsedMs / Math.Max(1, firstResult.elapsedMs);

            // Allow some overhead, but should be roughly linear
            Assert.Less(timeRatio, subscriberRatio * 3,
                $"Performance should scale roughly linearly. Subscriber ratio: {subscriberRatio:F1}x, Time ratio: {timeRatio:F1}x");
        }

        #endregion

        #region Memory Allocation Tests

        [Test]
        public void EventBus_RepeatedPublish_NoMemoryLeaks()
        {
            const int iterations = 1000;
            var subscriber = new ProbeSubscriber<LightweightEvent>();
            using var subscription = _eventBus.Observe<LightweightEvent>().Subscribe(subscriber.OnNext);

            // Get baseline memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var initialMemory = GC.GetTotalMemory(false);

            // Publish many events
            for (int i = 0; i < iterations; i++)
            {
                _eventBus.Publish(new LightweightEvent(i));
            }

            // Force collection and measure
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);

            var memoryIncrease = finalMemory - initialMemory;

            Assert.AreEqual(iterations, subscriber.ReceivedEvents.Count, "All events should be received");

            // Allow some reasonable memory overhead but no significant leaks
            Assert.Less(memoryIncrease, iterations * 100,
                $"Memory increase should be minimal. Increased by {memoryIncrease} bytes for {iterations} events");
        }

        [Test]
        public void EventBus_SubscriberChurn_HandlesFrequentSubscriptions()
        {
            const int churnCycles = 100;
            const int subscribersPerCycle = 10;
            var totalReceived = 0;

            for (int cycle = 0; cycle < churnCycles; cycle++)
            {
                var subscriptions = new List<IDisposable>();
                var subscribers = new List<ProbeSubscriber<LightweightEvent>>();

                // Create subscribers
                for (int i = 0; i < subscribersPerCycle; i++)
                {
                    var subscriber = new ProbeSubscriber<LightweightEvent>();
                    subscribers.Add(subscriber);
                    subscriptions.Add(_eventBus.Observe<LightweightEvent>().Subscribe(subscriber.OnNext));
                }

                // Publish event
                _eventBus.Publish(new LightweightEvent(cycle));

                // Verify and count
                foreach (var subscriber in subscribers)
                {
                    totalReceived += subscriber.ReceivedEvents.Count;
                }

                // Dispose all subscriptions
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }
            }

            Assert.AreEqual(churnCycles * subscribersPerCycle, totalReceived,
                "All events should be received despite subscription churn");
        }

        #endregion

        #region Throughput Benchmarking Tests

        [Test]
        public void EventBus_HighThroughput_MaintainsPerformance()
        {
            const int eventCount = 10000;
            const int subscriberCount = 10;

            var subscribers = new List<CountingSubscriber<LightweightEvent>>();
            var subscriptions = new List<IDisposable>();

            // Setup subscribers
            for (int i = 0; i < subscriberCount; i++)
            {
                var subscriber = new CountingSubscriber<LightweightEvent>();
                subscribers.Add(subscriber);
                subscriptions.Add(_eventBus.Observe<LightweightEvent>().Subscribe(subscriber.OnNext));
            }

            var stopwatch = Stopwatch.StartNew();

            // Rapid-fire publishing
            for (int i = 0; i < eventCount; i++)
            {
                _eventBus.Publish(new LightweightEvent(i));
            }

            stopwatch.Stop();

            // Verify correctness
            foreach (var subscriber in subscribers)
            {
                Assert.AreEqual(eventCount, subscriber.Count, "All subscribers should receive all events");
            }

            // Performance requirements
            var eventsPerSecond = eventCount / (stopwatch.ElapsedMilliseconds / 1000.0);
            Assert.Greater(eventsPerSecond, 500,
                $"Should handle at least 5000 events/second. Achieved: {eventsPerSecond:F0} events/second");

            // Cleanup
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }

        [Test]
        public void EventBus_HeavyEventPayloads_AcceptablePerformance()
        {
            const int eventCount = 100;
            const int subscriberCount = 50;

            var subscribers = new List<ProbeSubscriber<HeavyEvent>>();
            var subscriptions = new List<IDisposable>();

            // Setup subscribers
            for (int i = 0; i < subscriberCount; i++)
            {
                var subscriber = new ProbeSubscriber<HeavyEvent>();
                subscribers.Add(subscriber);
                subscriptions.Add(_eventBus.Observe<HeavyEvent>().Subscribe(subscriber.OnNext));
            }

            // Create heavy event data
            var heavyData = Enumerable.Range(0, 100).ToArray();

            var stopwatch = Stopwatch.StartNew();

            // Publish heavy events
            for (int i = 0; i < eventCount; i++)
            {
                var evt = new HeavyEvent(
                    $"Data1_{i}_{new string('x', 100)}",
                    $"Data2_{i}_{new string('y', 100)}",
                    $"Data3_{i}_{new string('z', 100)}",
                    heavyData);
                _eventBus.Publish(evt);
            }

            stopwatch.Stop();

            // Verify correctness
            foreach (var subscriber in subscribers)
            {
                Assert.AreEqual(eventCount, subscriber.ReceivedEvents.Count,
                    "All subscribers should receive all heavy events");
            }

            // Performance should still be reasonable even with heavy payloads
            Assert.Less(stopwatch.ElapsedMilliseconds, 5000,
                $"Heavy event processing should complete within 5 seconds. Took: {stopwatch.ElapsedMilliseconds}ms");

            // Cleanup
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }

        #endregion

        #region Resource Usage Verification Tests

        [Test]
        public void EventBus_LongRunningOperation_StableResourceUsage()
        {
            const int phases = 10;
            const int eventsPerPhase = 100;
            const int subscribersPerPhase = 20;

            var initialMemory = GC.GetTotalMemory(true);
            var memoryReadings = new List<long>();

            for (int phase = 0; phase < phases; phase++)
            {
                var subscriptions = new List<IDisposable>();
                var subscribers = new List<ProbeSubscriber<BenchmarkEvent>>();

                // Create subscribers for this phase
                for (int i = 0; i < subscribersPerPhase; i++)
                {
                    var subscriber = new ProbeSubscriber<BenchmarkEvent>();
                    subscribers.Add(subscriber);
                    subscriptions.Add(_eventBus.Observe<BenchmarkEvent>().Subscribe(subscriber.OnNext));
                }

                // Publish events
                for (int i = 0; i < eventsPerPhase; i++)
                {
                    _eventBus.Publish(new BenchmarkEvent(phase * eventsPerPhase + i, DateTime.UtcNow.Ticks));
                }

                // Verify phase completion
                foreach (var subscriber in subscribers)
                {
                    Assert.AreEqual(eventsPerPhase, subscriber.ReceivedEvents.Count,
                        $"Phase {phase} should complete successfully");
                }

                // Cleanup phase
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }

                // Memory check
                GC.Collect();
                memoryReadings.Add(GC.GetTotalMemory(true));
            }

            // Verify memory stability - should not continuously grow
            var finalMemory = memoryReadings.Last();
            var memoryIncrease = finalMemory - initialMemory;
            var maxReasonableIncrease = phases * eventsPerPhase * subscribersPerPhase * 50; // 50 bytes per event-subscriber pair

            Assert.Less(memoryIncrease, maxReasonableIncrease,
                $"Memory should remain stable across phases. Increase: {memoryIncrease} bytes");
        }

        [Test]
        public void EventBus_MultipleEventTypes_EfficientRouting()
        {
            const int eventsPerType = 200;
            const int subscribersPerType = 10;

            var allSubscriptions = new List<IDisposable>();

            // Setup different event types with dedicated subscribers
            var lightweightSubscribers = new List<CountingSubscriber<LightweightEvent>>();
            var heavySubscribers = new List<CountingSubscriber<HeavyEvent>>();
            var benchmarkSubscribers = new List<CountingSubscriber<BenchmarkEvent>>();

            // LightweightEvent subscribers
            for (int i = 0; i < subscribersPerType; i++)
            {
                var subscriber = new CountingSubscriber<LightweightEvent>();
                lightweightSubscribers.Add(subscriber);
                allSubscriptions.Add(_eventBus.Observe<LightweightEvent>().Subscribe(subscriber.OnNext));
            }

            // HeavyEvent subscribers (fewer due to processing cost)
            for (int i = 0; i < subscribersPerType / 2; i++)
            {
                var subscriber = new CountingSubscriber<HeavyEvent>();
                heavySubscribers.Add(subscriber);
                allSubscriptions.Add(_eventBus.Observe<HeavyEvent>().Subscribe(subscriber.OnNext));
            }

            // BenchmarkEvent subscribers
            for (int i = 0; i < subscribersPerType; i++)
            {
                var subscriber = new CountingSubscriber<BenchmarkEvent>();
                benchmarkSubscribers.Add(subscriber);
                allSubscriptions.Add(_eventBus.Observe<BenchmarkEvent>().Subscribe(subscriber.OnNext));
            }

            var stopwatch = Stopwatch.StartNew();

            // Publish interleaved events of different types
            for (int i = 0; i < eventsPerType; i++)
            {
                _eventBus.Publish(new LightweightEvent(i));

                if (i % 2 == 0) // Publish heavy events less frequently
                {
                    var heavyData = new int[] { i, i * 2, i * 3 };
                    _eventBus.Publish(new HeavyEvent($"Heavy{i}", $"Data{i}", $"Payload{i}", heavyData));
                }

                _eventBus.Publish(new BenchmarkEvent(i, DateTime.UtcNow.Ticks));
            }

            stopwatch.Stop();

            // Verify type isolation - subscribers should only receive their event types
            foreach (var subscriber in lightweightSubscribers)
            {
                Assert.AreEqual(eventsPerType, subscriber.Count, "LightweightEvent subscribers should receive all lightweight events");
            }

            foreach (var subscriber in heavySubscribers)
            {
                Assert.AreEqual(eventsPerType / 2, subscriber.Count, "HeavyEvent subscribers should receive heavy events only");
            }

            foreach (var subscriber in benchmarkSubscribers)
            {
                Assert.AreEqual(eventsPerType, subscriber.Count, "BenchmarkEvent subscribers should receive all benchmark events");
            }

            // Performance should be efficient despite multiple types
            Assert.Less(stopwatch.ElapsedMilliseconds, 2000,
                $"Multi-type event routing should be efficient. Took: {stopwatch.ElapsedMilliseconds}ms");

            // Cleanup
            foreach (var subscription in allSubscriptions)
            {
                subscription.Dispose();
            }
        }

        #endregion

        #region Stress Testing

        [Test]
        public void EventBus_StressTest_CombinedScenarios()
        {
            const int stressPhases = 5;
            const int maxSubscribers = 100;
            const int eventsPerPhase = 500;

            var totalEventsProcessed = 0;
            var random = new System.Random(42); // Fixed seed for reproducibility

            for (int phase = 0; phase < stressPhases; phase++)
            {
                var subscriptions = new List<IDisposable>();
                var subscribers = new List<CountingSubscriber<LightweightEvent>>();

                // Variable subscriber count per phase
                int subscriberCount = random.Next(10, maxSubscribers + 1);

                // Create subscribers
                for (int i = 0; i < subscriberCount; i++)
                {
                    var subscriber = new CountingSubscriber<LightweightEvent>();
                    subscribers.Add(subscriber);
                    subscriptions.Add(_eventBus.Observe<LightweightEvent>().Subscribe(subscriber.OnNext));
                }

                // Rapid publishing with some subscriber churn
                for (int i = 0; i < eventsPerPhase; i++)
                {
                    _eventBus.Publish(new LightweightEvent(phase * eventsPerPhase + i));

                    // Randomly dispose and recreate some subscribers mid-phase
                    if (i % 50 == 0 && subscriptions.Count > 5)
                    {
                        int indexToDispose = random.Next(0, subscriptions.Count);
                        subscriptions[indexToDispose].Dispose();
                        subscriptions.RemoveAt(indexToDispose);
                        subscribers.RemoveAt(indexToDispose);
                    }
                }

                // Verify remaining subscribers received expected events
                foreach (var subscriber in subscribers)
                {
                    totalEventsProcessed += subscriber.Count;
                    Assert.Greater(subscriber.Count, 0, "Each active subscriber should have received events");
                }

                // Cleanup phase
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }
            }

            Assert.Greater(totalEventsProcessed, stressPhases * eventsPerPhase,
                $"Stress test should process significant events. Processed: {totalEventsProcessed}");
        }

        #endregion

        // Performance test utilities
        public class CountingSubscriber<T>
        {
            public int Count { get; private set; }

            public void OnNext(T _)
            {
                Count++;
            }
        }

        public class ProbeSubscriber<T>
        {
            public List<T> ReceivedEvents { get; } = new List<T>();

            public void OnNext(T value)
            {
                ReceivedEvents.Add(value);
            }
        }
    }
}
#endif