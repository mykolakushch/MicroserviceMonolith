namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Represents a query in CQRS that retrieves data and produces a result of type <typeparamref name="TResult"/>.
    /// Queries should be idempotent and must not modify system state.
    /// </summary>
    /// <typeparam name="TResult">The type of the query result.</typeparam>
    public interface IQuery<out TResult> { }
}
