using System;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Contract for subscribing to events published across the application.
    /// </summary>
    public interface IEventSubscriber
    {
        /// <summary>
        /// Subscribes to an event with a cancellation-aware asynchronous handler.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
        /// <param name="handler">The asynchronous handler function.</param>
        /// <returns>A disposable subscription token to unsubscribe when disposed.</returns>
        IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;

        /// <summary>
        /// Subscribes to an event with a standard asynchronous handler.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
        /// <param name="handler">The asynchronous handler function.</param>
        /// <returns>A disposable subscription token to unsubscribe when disposed.</returns>
        IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class;
    }
}
