#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Frost9.EventBus
{
    /// <summary>
    /// Editor-only static class that automatically cleans up EventBus instances when exiting play mode
    /// to prevent memory leaks and ensure proper disposal of reactive subscriptions.
    /// </summary>
    [InitializeOnLoad]
    static class EventBusCleanup
    {
        /// <summary>
        /// Static constructor that registers for play mode state changes when the editor loads.
        /// </summary>
        static EventBusCleanup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Handles play mode state changes and disposes EventBus instances when exiting play mode.
        /// </summary>
        /// <param name="change">The play mode state change that occurred.</param>
        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                var holder = UnityEngine.Object.FindFirstObjectByType<EventBusHolder>();
                if (holder != null)
                {
                    (holder.Bus as IDisposable)?.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// MonoBehaviour wrapper that holds an IEventBus reference for automatic cleanup detection.
    /// Used by EventBusCleanup to find and dispose EventBus instances during play mode transitions.
    /// </summary>
    public class EventBusHolder : MonoBehaviour
    {
        /// <summary>
        /// Gets or sets the EventBus instance managed by this holder.
        /// </summary>
        public IEventBus Bus { get; set; }
    }
}
#endif