using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Queries;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class QueryBusTests
    {
        public record GetGreetingQuery(string Name) : IQuery<string>;

        private class GetGreetingQueryHandler : IQueryHandler<GetGreetingQuery, string>
        {
            public Task<string> HandleAsync(GetGreetingQuery query, CancellationToken cancellationToken = default)
            {
                return Task.FromResult($"Hello, {query.Name}!");
            }
        }

        private class QueryTrackingMiddleware : IQueryMiddleware
        {
            private readonly string _name;
            private readonly List<string> _trace;

            public QueryTrackingMiddleware(string name, List<string> trace)
            {
                _name = name;
                _trace = trace;
            }

            public async Task<TResult> InvokeAsync<TQuery, TResult>(
                TQuery query,
                Func<Task<TResult>> next,
                CancellationToken cancellationToken = default)
                where TQuery : IQuery<TResult>
            {
                _trace.Add($"{_name}:Before");
                var result = await next();
                _trace.Add($"{_name}:After");
                return result;
            }
        }

        [Fact]
        public async Task QueryAsync_ExecutesHandlerAndReturnsResult()
        {
            var bus = new QueryBus();
            bus.RegisterHandler(new GetGreetingQueryHandler());

            var result = await bus.QueryAsync(new GetGreetingQuery("Alice"));

            result.Should().Be("Hello, Alice!");
        }

        [Fact]
        public async Task QueryAsync_UnregisteredQuery_ThrowsInvalidOperationException()
        {
            var bus = new QueryBus();

            Func<Task> act = async () => await bus.QueryAsync(new GetGreetingQuery("Bob"));

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*No query handler registered*");
        }

        [Fact]
        public async Task QueryAsync_ExecutesMiddlewarePipeline()
        {
            var bus = new QueryBus();
            bus.RegisterHandler(new GetGreetingQueryHandler());

            var trace = new List<string>();
            bus.AddMiddleware(new QueryTrackingMiddleware("QM1", trace));
            bus.AddMiddleware(new QueryTrackingMiddleware("QM2", trace));

            var result = await bus.QueryAsync(new GetGreetingQuery("Charlie"));

            result.Should().Be("Hello, Charlie!");
            trace.Should().ContainInOrder("QM1:Before", "QM2:Before", "QM2:After", "QM1:After");
        }

        [Fact]
        public async Task QueryAsync_ResolvesHandlerFromServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddTransient<IQueryHandler<GetGreetingQuery, string>, GetGreetingQueryHandler>();

            var sp = services.BuildServiceProvider();
            var bus = new QueryBus(sp);

            var result = await bus.QueryAsync(new GetGreetingQuery("Dave"));

            result.Should().Be("Hello, Dave!");
        }

        [Fact]
        public async Task QueryAsync_PropagatesCancellationToken()
        {
            var bus = new QueryBus();
            bus.RegisterHandler(new GetGreetingQueryHandler());

            var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> act = async () => await bus.QueryAsync(new GetGreetingQuery("Eve"), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
