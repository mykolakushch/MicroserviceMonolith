using System;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Declares that executing the command more than once has the same effect as executing it once, so it is safe
    /// for <see cref="RetryCommandMiddleware"/> to retry it after a failure. Only mark a command when its handler
    /// really is idempotent (naturally, or through an idempotency key / deduplication).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public sealed class IdempotentCommandAttribute : Attribute
    {
    }
}
