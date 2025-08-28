#if UNITY_INCLUDE_TESTS
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Frost9.EventBus.Tests
{
    /// <summary>
    /// Helper classes for testing threading behavior and coordinating multi-threaded test scenarios.
    /// </summary>
    public static class ThreadingHelpers
    {
        /// <summary>
        /// Executes an action on a background thread and waits for completion.
        /// </summary>
        /// <param name="action">The action to execute on a background thread.</param>
        /// <param name="timeoutMs">Maximum time to wait for completion in milliseconds.</param>
        public static void RunOnBackgroundThread(Action action, int timeoutMs = 5000)
        {
            var task = Task.Run(action);
            if (!task.Wait(timeoutMs))
            {
                throw new TimeoutException($"Background thread operation did not complete within {timeoutMs}ms");
            }
        }

        /// <summary>
        /// Executes a function on a background thread and returns the result.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="func">The function to execute on a background thread.</param>
        /// <param name="timeoutMs">Maximum time to wait for completion in milliseconds.</param>
        /// <returns>The result of the function execution.</returns>
        public static T RunOnBackgroundThread<T>(Func<T> func, int timeoutMs = 5000)
        {
            var task = Task.Run(func);
            if (!task.Wait(timeoutMs))
            {
                throw new TimeoutException($"Background thread operation did not complete within {timeoutMs}ms");
            }
            return task.Result;
        }

        /// <summary>Runs the action on a single dedicated OS thread (not the thread pool).</summary>
        public static void RunOnDedicatedThread(Action action, int timeoutMs = 5000)
        {
            using var done = new ManualResetEvent(false);
            Exception error = null;
            var t = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            })
            { IsBackground = true }
 ;
            t.Start();
            if (!done.WaitOne(timeoutMs))
                throw new TimeoutException($"Dedicated thread operation did not complete within {timeoutMs}ms");
            if (error != null) throw new AggregateException(error);
        }

        /// <summary>Runs the func on a single dedicated OS thread (not the thread pool) and returns a result.</summary>
        public static T RunOnDedicatedThread<T>(Func<T> func, int timeoutMs = 5000)
        {
            using var done = new ManualResetEvent(false);
            Exception error = null;
            T result = default;
            var t = new Thread(() =>
            {
                try { result = func(); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            })
            { IsBackground = true }
        ;
            t.Start();
            if (!done.WaitOne(timeoutMs))
                throw new TimeoutException($"Dedicated thread operation did not complete within {timeoutMs}ms");
            if (error != null) throw new AggregateException(error);
            return result;
        }
    }

    /// <summary>
    /// Helper for coordinating actions between multiple threads in tests.
    /// Provides signaling and synchronization primitives.
    /// </summary>
    public class ThreadBarrier
    {
        private readonly ManualResetEventSlim _event = new(false);
        private volatile bool _disposed = false;

        /// <summary>
        /// Signals all waiting threads to proceed.
        /// </summary>
        public void Signal()
        {
            if (!_disposed)
                _event.Set();
        }

        /// <summary>
        /// Waits for the signal with an optional timeout.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait in milliseconds. Default is 5000ms.</param>
        /// <returns>True if the signal was received, false if timeout occurred.</returns>
        public bool Wait(int timeoutMs = 5000)
        {
            if (_disposed) return false;
            return _event.Wait(timeoutMs);
        }

        /// <summary>
        /// Resets the barrier so it can be used again.
        /// </summary>
        public void Reset()
        {
            if (!_disposed)
                _event.Reset();
        }

        /// <summary>
        /// Disposes the barrier and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _event.Set(); // Release any waiting threads
                _event.Dispose();
            }
        }
    }
}
#endif