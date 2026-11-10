using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Execution;
using MicroserviceMonolith.Core.Modules;
using MicroserviceMonolith.Core.Outbox;
using MicroserviceMonolith.Core.Queries;
using MicroserviceMonolith.Adapters.UserActions;

namespace FrameworkExample
{
    // =========================================================================
    // 1. DOMAIN MESSAGES (Contracts for Cross-Module Communication)
    // =========================================================================

    public record OrderItem(string ProductId, int Quantity, decimal UnitPrice);

    public record CreateOrderCommand(string CustomerId, List<OrderItem> Items) : ICommand<CreateOrderResult>;

    public record CreateOrderResult(Guid OrderId, decimal TotalAmount);

    public record GetStockQuery(string ProductId) : IQuery<StockResponse>;

    public record StockResponse(string ProductId, int AvailableQuantity);

    public record OrderCreatedEvent(Guid OrderId, string CustomerId, decimal TotalAmount) : EventBase;

    // =========================================================================
    // 2. ORDERING MODULE
    // =========================================================================

    public class OrderingModule : ModuleBase
    {
        public override string Name => "OrderingModule";

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Register command handlers and module-specific internal services
            services.AddTransient<ICommandHandler<CreateOrderCommand, CreateOrderResult>, CreateOrderCommandHandler>();
        }

        public override Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[{Name}] Initialized successfully.");
            return Task.CompletedTask;
        }

        public override Task ShutdownAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[{Name}] Gracefully shut down.");
            return Task.CompletedTask;
        }

        public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, CreateOrderResult>
        {
            private readonly IEventPublisher _eventPublisher;

            public CreateOrderCommandHandler(IEventPublisher eventPublisher)
            {
                _eventPublisher = eventPublisher;
            }

            public async Task<CreateOrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
            {
                decimal total = 0;
                foreach (var item in command.Items)
                {
                    total += item.Quantity * item.UnitPrice;
                }

                var orderId = Guid.NewGuid();
                Console.WriteLine($"[OrderingModule] Order created: {orderId} for customer {command.CustomerId}, Total: {total:C}");

                // Publish domain event to notify other modules
                await _eventPublisher.PublishAsync(new OrderCreatedEvent(orderId, command.CustomerId, total), cancellationToken);

                return new CreateOrderResult(orderId, total);
            }
        }
    }

    // =========================================================================
    // 3. INVENTORY MODULE
    // =========================================================================

    public class InventoryModule : ModuleBase
    {
        public override string Name => "InventoryModule";

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<InventoryState>();
            services.AddTransient<IQueryHandler<GetStockQuery, StockResponse>, GetStockQueryHandler>();
            services.AddTransient<IEventHandler<OrderCreatedEvent>, OrderCreatedInventoryHandler>();
        }

        public class InventoryState
        {
            private readonly Dictionary<string, int> _stock = new()
            {
                ["PROD-001"] = 150,
                ["PROD-002"] = 75
            };

            public int GetStock(string productId) => _stock.TryGetValue(productId, out var qty) ? qty : 0;

            public void ReserveStock(string productId, int quantity)
            {
                if (_stock.ContainsKey(productId))
                {
                    _stock[productId] = Math.Max(0, _stock[productId] - quantity);
                }
            }
        }

        public class GetStockQueryHandler : IQueryHandler<GetStockQuery, StockResponse>
        {
            private readonly InventoryState _state;

            public GetStockQueryHandler(InventoryState state) => _state = state;

            public Task<StockResponse> HandleAsync(GetStockQuery query, CancellationToken cancellationToken = default)
            {
                var qty = _state.GetStock(query.ProductId);
                return Task.FromResult(new StockResponse(query.ProductId, qty));
            }
        }

        public class OrderCreatedInventoryHandler : IEventHandler<OrderCreatedEvent>
        {
            private readonly InventoryState _state;

            public OrderCreatedInventoryHandler(InventoryState state) => _state = state;

            public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
            {
                Console.WriteLine($"[InventoryModule] Received OrderCreatedEvent for order {@event.OrderId}. Reserving stock...");
                _state.ReserveStock("PROD-001", 1);
                return Task.CompletedTask;
            }
        }
    }

    // =========================================================================
    // 4. NOTIFICATION MODULE
    // =========================================================================

    public class NotificationModule : ModuleBase
    {
        public override string Name => "NotificationModule";

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddTransient<IEventHandler<OrderCreatedEvent>, OrderCreatedNotificationHandler>();
        }

        public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
        {
            private readonly ExecutionQueue _queue;

            public OrderCreatedNotificationHandler(ExecutionQueue queue)
            {
                _queue = queue;
            }

            public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
            {
                // Enqueue an asynchronous background notification via Channel without blocking the caller
                _queue.Enqueue(async ct =>
                {
                    await Task.Delay(100, ct); // Simulate email dispatch
                    Console.WriteLine($"[NotificationModule - Background Worker] Email sent to {@event.CustomerId}: Order {@event.OrderId} confirmed for {@event.TotalAmount:C}!");
                });

                return Task.CompletedTask;
            }
        }
    }

    // =========================================================================
    // 5. APPLICATION ENTRY POINT
    // =========================================================================

    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("==========================================================");
            Console.WriteLine("  MicroserviceMonolith: Modular Monolith Showcase");
            Console.WriteLine("==========================================================\n");

            // 1. Setup DI Container & Configuration
            var configuration = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();

            services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            });

            // 2. Configure MicroserviceMonolith with assembly scanning
            services.AddMicroserviceMonolith(options =>
            {
                options.Configuration = configuration;
                options.RegisterServicesFromAssemblyContaining<Program>();
                options.EnableOutbox = true;
                options.ExecutionQueueOptions.WorkerCount = 2;
            });

            // 3. Register Middlewares for Command, Query, and Event Pipelines
            services.AddSingleton<ICommandMiddleware>(sp =>
                new LoggingCommandMiddleware(sp.GetRequiredService<ILogger<LoggingCommandMiddleware>>()));
            services.AddSingleton<ICommandMiddleware>(_ =>
                new PerformanceCommandMiddleware(TimeSpan.FromMilliseconds(500), msg => Console.WriteLine(msg)));

            services.AddSingleton<IQueryMiddleware>(sp =>
                new LoggingQueryMiddleware(sp.GetRequiredService<ILogger<LoggingQueryMiddleware>>()));

            services.AddSingleton<IEventMiddleware>(sp =>
                new LoggingEventMiddleware(sp.GetRequiredService<ILogger<LoggingEventMiddleware>>()));

            // 4. Build Service Provider
            var serviceProvider = services.BuildServiceProvider();

            // 5. Initialize Modules
            var moduleManager = serviceProvider.GetRequiredService<ModuleManager>();
            await moduleManager.InitializeModulesAsync(serviceProvider);
            Console.WriteLine($"\nRegistered modules: {string.Join(", ", moduleManager.Modules.Select(m => m.Name))}\n");

            // 6. Demonstrate CQRS Query Bus
            Console.WriteLine("--- 1. Executing CQRS Query ---");
            var queryBus = serviceProvider.GetRequiredService<IQueryBus>();
            var stockBefore = await queryBus.QueryAsync(new GetStockQuery("PROD-001"));
            Console.WriteLine($"Query result: Product '{stockBefore.ProductId}' available stock = {stockBefore.AvailableQuantity}\n");

            // 7. Demonstrate CQRS Command Bus (Cross-module event flow)
            Console.WriteLine("--- 2. Executing CQRS Command ---");
            var commandBus = serviceProvider.GetRequiredService<ICommandBus>();
            var createOrderCmd = new CreateOrderCommand(
                CustomerId: "customer-42",
                Items: new List<OrderItem>
                {
                    new("PROD-001", 1, 99.95m),
                    new("PROD-002", 2, 49.50m)
                });

            var orderResult = await commandBus.SendAsync<CreateOrderCommand, CreateOrderResult>(createOrderCmd);
            Console.WriteLine($"Command returned result: OrderId={orderResult.OrderId}, Total={orderResult.TotalAmount:C}\n");

            // 8. Verify Query after Event Processing
            var stockAfter = await queryBus.QueryAsync(new GetStockQuery("PROD-001"));
            Console.WriteLine($"Query result after order: Product '{stockAfter.ProductId}' stock = {stockAfter.AvailableQuantity}\n");

            // 9. Demonstrate User Actions module
            Console.WriteLine("--- 3. Dispatching User Action ---");
            var eventBus = serviceProvider.GetRequiredService<EventBus>();
            var userAction = new UserAction(eventBus);

            userAction.RegisterAction("AddToCart", async ctx =>
            {
                Console.WriteLine($"[UserAction] Action '{ctx.Args}' initiated by sender '{ctx.Sender}'.");
                await Task.CompletedTask;
            });

            await userAction.DispatchActionAsync("AddToCart", new UserActionContext("WebUI", "PROD-001 (Qty: 2)"));

            // 10. Demonstrate Transactional Outbox
            Console.WriteLine("\n--- 4. Demonstrating Transactional Outbox Pattern ---");
            var outboxStore = serviceProvider.GetRequiredService<IOutboxStore>();
            var outboxProcessor = serviceProvider.GetRequiredService<IOutboxProcessor>();

            var outboxEvent = new OrderCreatedEvent(Guid.NewGuid(), "outbox-customer", 250m);
            await outboxStore.SaveAsync(new OutboxMessage
            {
                EventType = nameof(OrderCreatedEvent),
                Event = outboxEvent
            });
            Console.WriteLine($"Saved pending event {outboxEvent.EventId} to Outbox.");

            var processedCount = await outboxProcessor.ProcessBatchAsync();
            Console.WriteLine($"OutboxProcessor drained and published {processedCount} message(s).\n");

            // Allow background channel tasks to finish
            await Task.Delay(300);

            // 11. Graceful Shutdown
            Console.WriteLine("--- 5. Graceful Shutdown ---");
            await moduleManager.ShutdownModulesAsync(serviceProvider);

            var queue = serviceProvider.GetRequiredService<ExecutionQueue>();
            await queue.DisposeAsync();

            Console.WriteLine("\n[Application] Shutdown completed cleanly.");
        }
    }
}
