using System;

namespace MicroserviceMonolith.Core.UserActions
{
    public record UserActionContext
    {
        public object Sender{ get; init; }
        public object Args { get; init; }
    }
}
