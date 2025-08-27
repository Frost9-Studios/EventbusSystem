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

### Reactive Composition

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

### Async Patterns

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