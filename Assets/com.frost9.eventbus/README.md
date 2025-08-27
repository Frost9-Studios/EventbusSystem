# Frost9 EventBus

Generic, universal event bus system for Unity using R3 reactive extensions.

## Features

- **Generic**: Works with any `T` - no domain knowledge or named events
- **Universal**: Single bus instance routes all event types
- **Minimal**: No assembly scanning, reflection, or complex features
- **Main-thread only**: Safe publishing with warning for off-thread calls
- **R3-first**: Full reactive operator support with UniTask integration

## Quick Start

### Basic Usage

```csharp
using Frost9.EventBus;

// Define your event types (any struct/class)
readonly struct HealthChanged 
{ 
    public readonly int value; 
    public HealthChanged(int v) => value = v; 
}

// Subscribe to events
var bus = new EventBus();
using var subscription = bus.Observe<HealthChanged>()
    .SubscribeSafe(e => UpdateHealthBar(e.value));

// Publish events
bus.Publish(new HealthChanged(75));
```

## Complete Usage Examples

### 1) DI Setup (VContainer)

```csharp
// Composition root (e.g., on your LifetimeScope)
public class GameInstaller : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();
        builder.RegisterEntryPoint<BattleSystems>(); // optional
    }
}
```

### 2) Event Types (Commands vs State)

```csharp
public readonly struct DamageApplied 
{ 
    public readonly int TargetId, Amount; 
    public DamageApplied(int t, int a) { TargetId = t; Amount = a; } 
}

public readonly struct HealthChanged 
{ 
    public readonly int TargetId, Value;  
    public HealthChanged(int t, int v) { TargetId = t; Value = v; } 
}
```

### 3) A System that Publishes State from a Command

```csharp
using R3;

public sealed class HealthSystem : IStartable, IDisposable
{
    readonly IEventBus _bus;
    readonly CompositeDisposable _cd = new();
    readonly Dictionary<int,int> _hp = new();

    public HealthSystem(IEventBus bus) => _bus = bus;

    public void Start()
    {
        _cd.Add(_bus.Observe<DamageApplied>()
            .SubscribeSafe(cmd =>
            {
                var v = (_hp.TryGetValue(cmd.TargetId, out var h) ? h : 100) - cmd.Amount;
                v = Math.Max(v, 0);
                _hp[cmd.TargetId] = v;
                _bus.Publish(new HealthChanged(cmd.TargetId, v));
            }));
    }

    public void Dispose() => _cd.Dispose();
}
```

### 4) An Enemy that Listens Only to Its Own Health

```csharp
using R3;
using UnityEngine;

public sealed class Enemy : MonoBehaviour
{
    [SerializeField] int id;
    IEventBus _bus;
    CompositeDisposable _cd = new();

    public Enemy(IEventBus bus) => _bus = bus;

    void OnEnable()
    {
        _cd.Add(_bus.Observe<HealthChanged>()
            .Where(e => e.TargetId == id)                // filter by identity
            .DistinctUntilChanged(e => e.Value)          // drop duplicates
            .SampleFrame(1)                               // at most once per frame
            .SubscribeSafe(e => ApplyHealth(e.Value)));
    }

    void OnDisable() { _cd.Dispose(); _cd = new(); }

    void ApplyHealth(int v) { /* update UI/anim */ }
}
```

### 5) One-Shot Orchestration with Next<T>() + UniTask

```csharp
using Cysharp.Threading.Tasks;

public sealed class FaintWatcher : MonoBehaviour
{
    IEventBus _bus;
    public FaintWatcher(IEventBus bus) => _bus = bus;

    async UniTaskVoid WatchMyFaint(int myId)
    {
        // await the next matching event, auto-unsubscribes when it hits (or on cancel)
        var _ = await _bus.Next<HealthChanged>(e => e.TargetId == myId && e.Value <= 0,
                                               this.GetCancellationTokenOnDestroy());
        await PlayFaintAnimationAsync();
    }
}
```

### 6) Publishing from Anywhere (Player Hits Enemy B)

```csharp
// A controller, ability system, etc.
public void OnHitEnemy(int enemyId, int dmg)
{
    _bus.Publish(new DamageApplied(enemyId, dmg));
}
```

## Notes

- **One universal IEventBus instance** (DI singleton)
- **A channel = "events of type T"** - First `Observe<T>()` creates a Subject<T>; publish before observe is a no-op
- **SubscribeSafe** wraps your handler in try/catch so one bad subscriber doesn't break delivery
- **Main-thread publishing enforced** - do async work off-thread with UniTask, then `SwitchToMainThread()` before Publish

## Reactive Composition

```csharp
// Filter and transform with R3 operators
bus.Observe<HealthChanged>()
    .Where(e => e.value <= 0)
    .FirstAsync()
    .SubscribeSafe(_ => ShowGameOver());

// Throttle rapid events
bus.Observe<MouseMove>()
    .ThrottleFirst(TimeSpan.FromMilliseconds(16))
    .SubscribeSafe(HandleMouseMove);
```

## Async Patterns

```csharp
// Wait for next event
var evt = await bus.Next<HealthChanged>();

// Wait with predicate
var lowHealth = await bus.Next<HealthChanged>(e => e.value <= 20);

// With cancellation
var damage = await bus.Next<DamageEvent>(cancellationToken: ct);
```

## Dependency Injection

### VContainer Integration

```csharp
public class GameInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.RegisterInstance(new EventBusInstaller());
    }
}

// Inject anywhere
public class HealthSystem
{
    readonly IEventBus _bus;
    
    public HealthSystem(IEventBus bus) => _bus = bus;
}
```

### Manual Singleton

```csharp
public static class GlobalBus
{
    public static IEventBus Instance { get; } = new EventBus();
}
```

## Error Handling

Use `SubscribeSafe` for automatic exception isolation:

```csharp
bus.Observe<RiskyEvent>()
    .SubscribeSafe(e => 
    {
        // Exceptions logged automatically, won't crash other subscribers
        DoSomethingRisky(e);
    });
```

## Thread Safety

- **Publishing**: Main thread only (logs warning and ignores if off-thread)
- **Subscribing**: Main thread recommended
- **No cross-thread marshalling** (add if needed later)

## Lifecycle

```csharp
// Dispose to complete all streams and cleanup
bus.Dispose();

// Editor cleanup (automatic on play mode exit)
// Handled by EventBusCleanup when using EventBusHolder
```

## API Reference

### IEventBus
```csharp
public interface IEventBus
{
    void Publish<T>(in T evt);          // Publish event to all subscribers
    IObservable<T> Observe<T>();        // Get observable for event type
}
```

### Extensions
```csharp
// UniTask integration
UniTask<T> Next<T>(predicate?, CancellationToken?)

// Safe subscribing with exception isolation
IDisposable SubscribeSafe<T>(Action<T> onNext)
```

## Performance

- **Zero cost until first observer** - subjects created on-demand
- **No reflection** - typed dictionary lookup only  
- **No assembly scanning** - instance-based lifecycle
- **IL2CPP friendly** - no dynamic code generation

## Behavior Notes

- **Early publish is ignored** - Publishing before any observer subscribes will be dropped (no buffering)
- **On-demand subject creation** - Subjects only exist when someone is observing
- **No sticky/replay** - Events are not replayed to new subscribers

## Migration from Static EventBus

**Old:**
```csharp
EventBus<HealthChanged>.Publish(evt);
EventBus<HealthChanged>.Subscribe(handler);
```

**New:**
```csharp
bus.Publish(evt);
bus.Observe<HealthChanged>().Subscribe(handler);
```

## Requirements

- Unity 6000.0+
- R3 (Reactive Extensions)
- UniTask
- VContainer (optional, for DI)