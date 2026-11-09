using System.Threading;

namespace MicroserviceMonolith.Core.Context
{
    /// <summary>
    /// Provides access to the current ambient <see cref="CorrelationContext"/>.
    /// </summary>
    public interface ICorrelationContextAccessor
    {
        /// <summary>
        /// Gets or sets the current <see cref="CorrelationContext"/>.
        /// </summary>
        CorrelationContext? CorrelationContext { get; set; }
    }

    /// <summary>
    /// Default implementation of <see cref="ICorrelationContextAccessor"/> backed by an <see cref="AsyncLocal{T}"/>.
    /// </summary>
    public class CorrelationContextAccessor : ICorrelationContextAccessor
    {
        private static readonly AsyncLocal<CorrelationContext?> _current = new();

        /// <inheritdoc />
        public CorrelationContext? CorrelationContext
        {
            get => _current.Value;
            set => _current.Value = value;
        }
    }
}
