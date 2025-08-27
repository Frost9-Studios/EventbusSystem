#if UNITY_EDITOR
using UnityEditor;
using System;

namespace Frost9.EventBus.Editor
{
    [InitializeOnLoad]
    internal static class EventBusCleanup
    {
        static EventBusCleanup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        
        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                // Optional cleanup for manual singleton patterns
                // If using DI, container will handle disposal automatically
                if (GlobalEventBus.Instance is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }
    
    // Example singleton holder - only needed for manual instantiation
    public static class GlobalEventBus
    {
        public static IEventBus Instance { get; set; }
    }
}
#endif