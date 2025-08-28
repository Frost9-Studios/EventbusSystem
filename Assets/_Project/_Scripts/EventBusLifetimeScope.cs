using Frost9.EventBus;
using VContainer;
using VContainer.Unity;

public class EventBusLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterEventBus();

        // Register the test controller so it runs every frame
        builder.RegisterEntryPoint<BusTestController>(Lifetime.Singleton);
    }
}
