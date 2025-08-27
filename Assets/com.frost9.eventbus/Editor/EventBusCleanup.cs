#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Frost9.EventBus
{
    [InitializeOnLoad]
    static class EventBusCleanup
    {
        static EventBusCleanup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

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

    public class EventBusHolder : MonoBehaviour
    {
        public IEventBus Bus { get; set; }
    }
}
#endif