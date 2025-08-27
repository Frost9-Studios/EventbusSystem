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
        /// <param name="predicate">Optional predicate to filter events. If null, the first event of type T is returned.</param>
        /// <param name="ct">Cancellation token to cancel the awaiting operation.</param>
        /// <returns>A UniTask that completes when the next matching event is received.</returns>
        /// <remarks> Use this for one-time event handling when you want to wait once and stop listening after - built in clean up logic and lower overhead. </remarks>
        public static UniTask<T> Next<T>(
            this IEventBus bus,
            Func<T, bool>? predicate = null,
            CancellationToken ct = default)
        {
            var task = predicate == null
                ? bus.Observe<T>().FirstAsync()
                : bus.Observe<T>().FirstAsync(predicate);

            return task.AsUniTask().AttachExternalCancellation(ct);
        }
    }
}