# MicroserviceMonolith

[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-39%20Passed-brightgreen.svg)]()

**MicroserviceMonolith** is a lightweight, production-grade .NET framework for architecting robust **Modular Monoliths**. It bridges the operational simplicity of a unified single-process application with the architectural benefits of microservices: extreme loose coupling, bounded contexts, CQRS, and event-driven communication.

---

## Architecture Overview

```mermaid
flowchart TD
    subgraph Host ["Application Host (.NET Host / DI)"]
        MM["ModuleManager\n(Lifecycle: Init & Shutdown)"]
        
        subgraph Pipelines ["Pipeline Middlewares"]
            direction TB
            Log["LoggingMiddleware"]
            Retry["RetryMiddleware (Backoff + Jitter)"]
            Perf["PerformanceMiddleware"]
            Err["ErrorHandlingMiddleware"]
        end

        subgraph CQRS ["CQRS & Messaging Buses"]
            CB["CommandBus\n(ICommand, ICommand&lt;TResult&gt;)"]
            QB["QueryBus\n(IQuery&lt;TResult&gt;)"]
            EB["EventBus\n(Pub/Sub: Sequential / Parallel)"]
        end

        subgraph Background ["Background Processing"]
            EQ["ExecutionQueue\n(Channels FIFO Queue)"]
            OB["Transactional Outbox\n(OutboxStore & Processor)"]
        end

        subgraph Modules ["Encapsulated Business Modules"]
            M1["OrderingModule\n(Internal State & Handlers)"]
            M2["InventoryModule\n(Internal State & Handlers)"]
            M3["NotificationModule\n(Background Workers)"]
        end
    end

    CB --> Pipelines
    QB --> Pipelines
    EB --> Pipelines

    Pipelines --> M1
    Pipelines --> M2
    Pipelines --> M3

    M1 -.->|Publish Event| EB
    M1 -.->|Save to Outbox| OB
    OB -.->|Drains Events| EB
    EB -.->|Enqueue Background Task| EQ
```

---

## Features

- **CQRS Architecture**:
  - **Command Bus**: Dispatches commands with or without return values (`ICommand`, `ICommand<TResult>`) to scoped handlers.
  - **Query Bus**: Executes data queries (`IQuery<TResult>`) with strict read-only semantics.
- **Event Bus (Pub/Sub)**:
  - Loosely coupled in-memory event publishing (`IEventPublisher`, `IEventSubscriber`).
  - Supports multiple dispatch strategies: `Sequential` (with error isolation), `Parallel` (`Task.WhenAll`), and `Background`.
  - Thread-safe subscription registry with `IDisposable` tokens for clean unsubscription.
- **Pipeline Middlewares (Interceptors)**:
  - Russian-doll middleware pipeline for Commands, Queries, and Events.
  - Built-in interceptors:
    - **Logging**: Structured logging using `Microsoft.Extensions.Logging`.
    - **Retry**: Resilient execution with configurable attempts, exponential backoff, and random jitter.
    - **Performance**: Execution timing with configurable latency warning thresholds.
    - **Error Handling**: Graceful error logging and exception propagation.
- **Module Lifecycle Management**:
  - Encapsulate features in `IModule` implementations (`ConfigureServices`, `InitializeAsync`, `ShutdownAsync`).
  - Ordered startup and reverse (LIFO) graceful shutdown.
  - Automatic assembly discovery with DI integration.
- **Asynchronous Execution Queue**:
  - High-throughput background worker queue powered by `System.Threading.Channels.Channel<T>`.
  - Supports bounded backpressure modes (`Wait`, `DropOldest`, `DropWrite`) and multi-worker concurrency.
- **Transactional Outbox Pattern**:
  - Reliable cross-module event dispatching with `IOutboxStore` and `IOutboxProcessor`.
  - Built-in thread-safe in-memory store.
- **User Actions Adapter**:
  - Adapter module converting external UI/API triggers into domain events and commands.

---

## Installation

Add the package to your .NET project:

```sh
# Using .NET CLI
dotnet add package MicroserviceMonolith --version 1.0.0
```

```sh
# Using Package Manager Console
Install-Package MicroserviceMonolith -Version 1.0.0
```

---

## Quickstart

### 1. Register with Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MicroserviceMonolith.Core.DependencyInjection;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.AddMicroserviceMonolith(options =>
    {
        // Scan current assembly for modules, command/query handlers, and event handlers
        options.RegisterServicesFromAssemblyContaining<Program>();
        options.Configuration = context.Configuration;
        options.EnableOutbox = true;
        options.ExecutionQueueOptions.WorkerCount = 2;
    });
});

var app = builder.Build();
```

---

## Defining Modules and CQRS

### 1. Defining Commands & Queries

```csharp
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Queries;

// Command returning a result
public record CreateOrderCommand(string CustomerId, decimal Amount) : ICommand<Guid>;

// Command without return value
public record CancelOrderCommand(Guid OrderId) : ICommand;

// Query returning data
public record GetOrderSummaryQuery(Guid OrderId) : IQuery<OrderSummaryDto>;
public record OrderSummaryDto(Guid OrderId, string CustomerId, decimal Amount);
```

### 2. Defining Domain Events

```csharp
using MicroserviceMonolith.Core.Events;

public record OrderCreatedEvent(Guid OrderId, string CustomerId, decimal Amount) : EventBase;
```

### 3. Creating an Encapsulated Module

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Modules;

public class OrderingModule : ModuleBase
{
    public override string Name => "Ordering";

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register private services & handlers
        services.AddTransient<ICommandHandler<CreateOrderCommand, Guid>, CreateOrderHandler>();
    }

    public override Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Ordering] Initialized.");
        return Task.CompletedTask;
    }

    public override Task ShutdownAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Ordering] Shut down.");
        return Task.CompletedTask;
    }

    // Handlers can be internal to preserve module boundary encapsulation
    public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Guid>
    {
        private readonly IEventPublisher _eventPublisher;

        public CreateOrderHandler(IEventPublisher eventPublisher)
        {
            _eventPublisher = eventPublisher;
        }

        public async Task<Guid> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
        {
            var orderId = Guid.NewGuid();
            
            // Publish event to notify other modules
            await _eventPublisher.PublishAsync(
                new OrderCreatedEvent(orderId, command.CustomerId, command.Amount), 
                cancellationToken);

            return orderId;
        }
    }
}
```

### 4. Subscribing to Events Across Modules

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Execution;

public class NotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ExecutionQueue _queue;

    public NotificationHandler(ExecutionQueue queue)
    {
        _queue = queue;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Offload email dispatch to the background channel queue
        _queue.Enqueue(async ct =>
        {
            await Task.Delay(50, ct);
            Console.WriteLine($"[Email Worker] Notification sent for order {@event.OrderId}!");
        });

        return Task.CompletedTask;
    }
}
```

---

## Running the Showcase Application

To execute the working example with `OrderingModule`, `InventoryModule`, and `NotificationModule`:

```sh
cd FrameworkExample
dotnet run
```

---

## Running Tests

The test suite covers CommandBus, QueryBus, EventBus concurrency, ExecutionQueue channels, ModuleManager lifecycle, Middlewares, Outbox, and DI scanning:

```sh
dotnet test
```

---

## License

This project is licensed under the [MIT License](LICENSE).
