using System;
using R3;

namespace Frost9.EventBus
{
    public interface IEventBus
    {
        void Publish<T>(in T evt);
        IObservable<T> Observe<T>();
    }
}