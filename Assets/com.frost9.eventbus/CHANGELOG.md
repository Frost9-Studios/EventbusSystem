# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-08-29

### Added
- Generic, universal event bus system using R3 reactive extensions
- Main-thread only publishing with safety warnings
- Zero reflection, minimal API surface design
- `IEventBus` interface with `Publish<T>()` and `Observe<T>()` methods
- `EventBus` concrete implementation with on-demand subject creation
- UniTask integration with `Next<T>()` extension method for async patterns
- `SubscribeSafe()` extension for automatic exception isolation
- VContainer integration via `RegisterEventBus()` extension method
- Comprehensive test suite covering core functionality, edge cases, and performance
- Thread safety enforcement (main thread only)
- Automatic cleanup and disposal handling
- Editor cleanup utilities for play mode transitions

### Features
- **Generic**: Works with any `T` type - structs, classes, records
- **Universal**: Single bus instance routes all event types  
- **Reactive**: Full R3 operator support (Where, ThrottleFirst, etc.)
- **Async**: UniTask integration for awaiting events
- **DI Ready**: VContainer extension method for easy integration
- **Safe**: Exception isolation with SubscribeSafe wrapper
- **Performant**: Zero cost until first observer, no assembly scanning
- **IL2CPP Friendly**: No reflection or dynamic code generation

### Dependencies
- Unity 6000.0+
- R3 (Reactive Extensions) 1.0.0+
- UniTask 2.3.3+
- VContainer (optional, for DI integration)