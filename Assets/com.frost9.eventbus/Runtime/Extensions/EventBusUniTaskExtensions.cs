#nullable enable
using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Threading;

namespace Frost9.EventBus
{
    /// <summary>
    /// Extension methods for IEventBus that provide UniTask integration for async/await patterns.
    /// </summary>
    public static class EventBusUniTaskExtensions
    {
        /// <summary>
        /// Awaits the next event of type T from the event bus, optionally filtered by a predicate.
        /// </summary>
        /// <typeparam name="T">The type of event to await.</typeparam>
        /// <param name="bus">The event bus to observe.</param>
        /// <param name="predicate">
        /// Optional predicate to filter events. If null, the first event of type T is returned.
        /// </param>
        /// <param name="ct">Cancellation token to cancel the awaiting operation.</param>
        /// <returns>A UniTask that completes when the next matching event is received.</returns>
        /// <remarks>
        /// Race-safe and game-ready:
        /// - If the event arrives first, it wins; if cancellation fires first, it wins.
        /// - Predicate exceptions are caught and the task faults (no unhandled Unity logs).
        /// - Subscription and cancellation registration are cleaned up exactly once.
        /// </remarks>
        public static UniTask<T> Next<T>(
            this IEventBus bus,
            Func<T, bool>? predicate = null,
            CancellationToken ct = default)
        {
            if (bus is null) throw new ArgumentNullException(nameof(bus));

            var tcs = new UniTaskCompletionSource<T>();
            IDisposable? subscription = null;
            CancellationTokenRegistration ctr = default;
            int completed = 0; // 0 = pending, 1 = finished

            void TryFinishWithResult(T value)
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                try { ctr.Dispose(); } catch { /* ignore */ }
                try { subscription?.Dispose(); } catch { /* ignore */ }
                tcs.TrySetResult(value);
            }

            void TryFinishFault(Exception ex)
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                try { ctr.Dispose(); } catch { /* ignore */ }
                try { subscription?.Dispose(); } catch { /* ignore */ }
                tcs.TrySetException(ex);
            }

            void TryFinishCanceled()
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                try { subscription?.Dispose(); } catch { /* ignore */ }
                tcs.TrySetCanceled(ct);
            }

            // Subscribe first
            var observable = bus.Observe<T>();
            subscription = observable.Subscribe(
                v =>
                {
                    if (predicate is null)
                    {
                        TryFinishWithResult(v);
                        return;
                    }

                    bool match;
                    try
                    {
                        match = predicate(v); // may throw
                    }
                    catch (Exception ex)
                    {
                        // Fault deterministically instead of letting R3 log an unhandled exception
                        TryFinishFault(ex);
                        return;
                    }

                    if (match) TryFinishWithResult(v);
                },
                _ =>
                {
                    // Bus stream completed (e.g., EventBus disposed) → surface a meaningful fault
                    if (Interlocked.Exchange(ref completed, 1) == 0)
                    {
                        try { ctr.Dispose(); } catch { /* ignore */ }
                        tcs.TrySetException(new ObjectDisposedException(nameof(IEventBus)));
                    }
                });

            // Then register cancellation
            if (ct.CanBeCanceled)
            {
                ctr = ct.Register(static s => ((Action)s!).Invoke(), (Action)TryFinishCanceled);

                // If already canceled now, catch up deterministically
                if (ct.IsCancellationRequested)
                {
                    TryFinishCanceled();
                }
            }

            return tcs.Task;
        }
    }
}