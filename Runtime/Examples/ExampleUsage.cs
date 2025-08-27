using System.Threading;
using Cysharp.Threading.Tasks;
using Frost9.EventBus;
using Frost9.EventBus.Extensions;
using R3;
using UnityEngine;

namespace Frost9.EventBus.Examples
{
    // Example events - no marker interface needed, any type works
    public readonly struct HealthChanged
    {
        public readonly int value;
        public HealthChanged(int v) => value = v;
    }
    
    public readonly struct PlayerDied
    {
        public readonly string cause;
        public PlayerDied(string cause) => this.cause = cause;
    }
    
    public class ExampleUsage : MonoBehaviour
    {
        private IEventBus _bus;
        private readonly CompositeDisposable _cd = new();
        
        void Start()
        {
            // Create bus instance (or inject via DI)
            _bus = new EventBus();
            
            // Simple subscription
            _cd.Add(_bus.Observe<HealthChanged>()
                .SubscribeSafe(OnHealthChanged));
            
            // Composition with R3 operators
            _cd.Add(_bus.Observe<HealthChanged>()
                .Where(e => e.value <= 0)
                .FirstAsync()
                .SubscribeSafe(_ => ShowGameOver()));
            
            // Test publishing
            _bus.Publish(new HealthChanged(100));
            _bus.Publish(new HealthChanged(0)); // Triggers game over
            
            // Async example
            _ = WaitForPlayerDeath(destroyCancellationToken);
        }
        
        void OnHealthChanged(HealthChanged evt)
        {
            Debug.Log($"Health: {evt.value}");
        }
        
        void ShowGameOver()
        {
            Debug.Log("Game Over!");
        }
        
        async UniTask WaitForPlayerDeath(CancellationToken ct)
        {
            var deathEvent = await _bus.Next<PlayerDied>(ct: ct);
            Debug.Log($"Player died: {deathEvent.cause}");
        }
        
        void OnDestroy()
        {
            _cd?.Dispose();
            (_bus as EventBus)?.Dispose();
        }
    }
}