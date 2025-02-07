using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Modules;

namespace MicroserviceMonolith.Core.UserActions
{
    /// <summary>
    /// Module for converting user actions into events.
    /// </summary>
    public class UserAction : ModuleBase
    {
        private readonly IEventPublisher _eventPublisher;
        private readonly Dictionary<string, Func<UserActionContext, Task>> _actionMappings = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="UserAction"/> module.
        /// </summary>
        /// <param name="eventPublisher">Event publisher to publish generated events.</param>
        public UserAction(IEventPublisher eventPublisher)
        {
            _eventPublisher = eventPublisher;
        }

        /// <summary>
        /// Registers a user action with an event factory function that generates an event based on the action context.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <param name="eventFactory">The function that generates an event based on the action context.</param>
        public void RegisterAction(string actionName, Func<UserActionContext, Task> eventFactory)
        {
            _actionMappings[actionName] = eventFactory;
        }

        /// <summary>
        /// Dispatches a user action by converting it into an event using the registered event factory.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <param name="context">The context of the action.</param>
        /// <returns>A task that represents the asynchronous operation of dispatching the event.</returns>
        /// <exception cref="ArgumentException">Thrown when the action is not registered.</exception>
        public Task DispatchActionAsync(string actionName, UserActionContext context)
        {
            if (_actionMappings.TryGetValue(actionName, out var factory))
            {
                return factory(context);
            }
            throw new ArgumentException($"Action '{actionName}' is not registered.");
        }
    }
}
