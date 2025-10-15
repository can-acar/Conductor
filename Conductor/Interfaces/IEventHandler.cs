using Conductor.Core;

namespace Conductor.Interfaces;

public interface IEventHandler<TEvent>
{
    Task Handle(Event<TEvent> eventData);
}