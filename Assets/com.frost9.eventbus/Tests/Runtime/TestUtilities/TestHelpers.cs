#if UNITY_INCLUDE_TESTS
using R3;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Mutable counter so tests can observe completion counts after subscription.
    /// </summary>
    public sealed class CompletionCounter
    {
        public int Count;
    }

    /// <summary>
    /// Helper methods and extensions to reduce test boilerplate and provide common testing utilities.
    /// </summary>
    public static class TestHelpers
    {
        /// <summary>
        /// Subscribe to an observable and automatically collect all emitted values in a list.
        /// Returns both the subscription (for disposal) and the list of recorded values.
        /// </summary>
        /// <typeparam name="T">The type of values emitted by the observable.</typeparam>
        /// <param name="source">The observable to subscribe to.</param>
        /// <returns>A tuple containing the subscription and the list that will collect values.</returns>
        public static (IDisposable subscription, List<T> recordedValues)
            SubscribeAndRecord<T>(this Observable<T> source)
        {
            var list = new List<T>();
            var sub = source.Subscribe(list.Add);
            return (sub, list);
        }

        /// <summary>
        /// Subscribe to an observable with both onNext and onCompleted handlers,
        /// collecting values and tracking completion events.
        /// </summary>
        /// <typeparam name="T">The type of values emitted by the observable.</typeparam>
        /// <param name="source">The observable to subscribe to.</param>
        /// <returns>A tuple containing subscription, recorded values, and completion count.</returns>
        public static (IDisposable subscription, List<T> recordedValues, CompletionCounter completed)
            SubscribeAndRecordWithCompletion<T>(this Observable<T> source)
        {
            var list = new List<T>();
            var counter = new CompletionCounter();
            var sub = source.Subscribe(
            list.Add,
            _ => Interlocked.Increment(ref counter.Count));
            return (sub, list, counter);
        }
    }

    /// <summary>
    /// Test subscriber that records all received values for verification in tests.
    /// Provides access to received values, completion status, and error information.
    /// </summary>
    /// <typeparam name="T">The type of values this subscriber can receive.</typeparam>
    public class ProbeSubscriber<T>
    {
        public List<T> ReceivedValues { get; } = new List<T>();
        public int CompletedCount { get; private set; }
        public Exception LastError { get; private set; }

        public void OnNext(T value) => ReceivedValues.Add(value);
        public void OnCompleted(Result result)
        {
            CompletedCount++;
            if (!result.IsSuccess)
                LastError = result.Exception;
        }

        public void Reset()
        {
            ReceivedValues.Clear();
            CompletedCount = 0;
            LastError = null;
        }
    }

    /// <summary>
    /// Test subscriber that counts delivery events without storing the actual values.
    /// Useful for performance tests and scenarios where only the count matters.
    /// </summary>
    /// <typeparam name="T">The type of values this subscriber can receive.</typeparam>
    public class CountingSubscriber<T>
    {
        public int DeliveryCount { get; private set; }
        public T LastValue { get; private set; }

        public void OnNext(T value)
        {
            DeliveryCount++;
            LastValue = value;
        }

        public void Reset()
        {
            DeliveryCount = 0;
            LastValue = default;
        }
    }

    /// <summary>
    /// Test subscriber that throws exceptions for testing error isolation.
    /// Can be configured to throw on specific events or always throw.
    /// </summary>
    /// <typeparam name="T">The type of values this subscriber can receive.</typeparam>
    public class ThrowingSubscriber<T>
    {
        private readonly Func<T, bool> _shouldThrow;
        public int ThrowCount { get; private set; }

        public ThrowingSubscriber(Func<T, bool> shouldThrow = null)
        {
            _shouldThrow = shouldThrow ?? (_ => true); // Default: always throw
        }

        public void OnNext(T value)
        {
            if (_shouldThrow(value))
            {
                ThrowCount++;
                throw new InvalidOperationException($"Test exception for value: {value}");
            }
        }

        public void Reset()
        {
            ThrowCount = 0;
        }
    }
}
#endif