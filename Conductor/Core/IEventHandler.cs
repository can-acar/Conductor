namespace Conductor.Core;

public interface IEventHandler<TEvent>
{
    Task Handle(Event<TEvent> eventData);
}