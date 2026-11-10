using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Events;

namespace MicroserviceMonolith.Adapters.UserActions
{
    /// <summary>
    /// Adapter for capturing user interactions and dispatching them into domain events or commands.
    /// Thread-safe and supports cancellation tokens.
    /// </summary>
    public class UserAction
    {
        private readonly IEventPublisher _eventPublisher;
        private readonly ConcurrentDictionary<string, Func<UserActionContext, CancellationToken, Task>> _actionMappings = new();

        /// <summary>
        /// Gets the associated event publisher.
        /// </summary>
        public IEventPublisher EventPublisher => _eventPublisher;

        public UserAction(IEventPublisher eventPublisher)
        {
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        }

        /// <summary>
        /// Registers a user action with a handler that processes the action context asynchronously.
        /// </summary>
        public void RegisterAction(string actionName, Func<UserActionContext, Task> eventFactory)
        {
            if (string.IsNullOrWhiteSpace(actionName)) throw new ArgumentException("Action name cannot be empty.", nameof(actionName));
            if (eventFactory == null) throw new ArgumentNullException(nameof(eventFactory));

            _actionMappings[actionName] = (ctx, _) => eventFactory(ctx);
        }

        /// <summary>
        /// Registers a cancellation-aware action handler.
        /// </summary>
        public void RegisterAction(string actionName, Func<UserActionContext, CancellationToken, Task> eventFactory)
        {
            if (string.IsNullOrWhiteSpace(actionName)) throw new ArgumentException("Action name cannot be empty.", nameof(actionName));
            if (eventFactory == null) throw new ArgumentNullException(nameof(eventFactory));

            _actionMappings[actionName] = eventFactory;
        }

        /// <summary>
        /// Dispatches a registered user action asynchronously.
        /// </summary>
        public Task DispatchActionAsync(string actionName, UserActionContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(actionName)) throw new ArgumentException("Action name cannot be empty.", nameof(actionName));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (_actionMappings.TryGetValue(actionName, out var handler))
            {
                return handler(context, cancellationToken);
            }

            throw new ArgumentException($"Action '{actionName}' is not registered.", nameof(actionName));
        }
    }
}
