using System;

namespace MicroserviceMonolith.Core.UserActions
{
    /// <summary>
    /// Represents the context of a user action, including the sender and associated arguments.
    /// </summary>
    public record UserActionContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserActionContext"/> with the sender and arguments.
        /// </summary>
        /// <param name="sender">The source of the action (e.g., the user, UI element, etc.).</param>
        /// <param name="args">The arguments related to the action (e.g., data, parameters, etc.).</param>
        public UserActionContext(object sender, object args)
        {
            Sender = sender;
            Args = args;
        }

        /// <summary>
        /// Gets the arguments associated with the user action.
        /// </summary>
        public object Args { get; init; }

        /// <summary>
        /// Gets the sender of the user action.
        /// </summary>
        public object Sender { get; init; }
    }
}
