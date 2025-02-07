using System;

namespace MicroserviceMonolith.Core.UserActions
{
    public record UserActionContext
    {

        public UserActionContext(object sender, object args)
        {
            Sender = sender;
            Args = args;
        }

        public object Args { get; init; }
        public object Sender { get; init; }
    }
}
