# Frost9 Event Bus

Universal R3-based reactive event system for Unity 6.

## Features

- **Generic**: Works with any type - no marker interfaces required
- **Universal**: Single bus instance handles all event types  
- **R3-powered**: Full reactive programming support with operators
- **Main-thread safe**: Automatic thread checking with warnings
- **Zero-cost until observed**: Subjects created on-demand
- **IL2CPP friendly**: No reflection in hot paths

## Quick Start

```csharp
using Frost9.EventBus;

// Define your events (any type works)
public readonly struct HealthChanged 
{
    public readonly int value;
    public HealthChanged(int v) => value = v;
}

// Create bus instance
IEventBus bus = new EventBus();

// Subscribe
bus.Observe<HealthChanged>()
   .SubscribeSafe(e => Debug.Log($"Health: {e.value}"))
   .AddTo(this);

// Publish
bus.Publish(new HealthChanged(75));
```

## R3 Composition

```csharp
// Filter, transform, and compose
bus.Observe<HealthChanged>()
   .Where(e => e.value <= 0)
   .FirstAsync()
   .SubscribeSafe(_ => ShowGameOver())
   .AddTo(this);
```

## Async/Await

```csharp
using Frost9.EventBus.Extensions;

// Wait for next event
var evt = await bus.Next<HealthChanged>(
    predicate: e => e.value <= 0,
    ct: cancellationToken);
```

## DI Integration (VContainer)

```csharp
using Frost9.EventBus.DI;

public class GameInstaller : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterEventBus();
    }
}
```

## Dependencies

- R3 (Reactive Extensions)
- UniTask (async/await support)
- VContainer (optional DI)

## License

MIT