# MicroserviceMonolith

**MicroserviceMonolith** is a modular monolith framework that combines the simplicity of a traditional monolith with the benefits of microservices. It provides loosely coupled modules with built-in event and command buses, module management, and an execution queue.

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Example Application](#example-application)
- [Contributing](#contributing)
- [License](#license)

## Features

- **Event Bus**: Publish/subscribe pattern with middleware support.
- **Command Bus**: Centralized command processing.
- **Module Management**: Encapsulated modules with lifecycle management.
- **Execution Queue**: Asynchronous task processing.
- **User Actions**: Event-driven user interactions.

## Installation

```sh
# Using .NET CLI
dotnet add package MicroserviceMonolith --version 1.0.0
```

```sh
# Using Package Manager Console
Install-Package MicroserviceMonolith -Version 1.0.0
```

## Example Application

Below is an example of how to use the framework.

### Defining Events

```csharp
namespace ExampleApp.Events
{
    /// <summary>
    /// Event to request a weather update.
    /// </summary>
    public record WeatherUpdateRequestEvent();
    
    /// <summary>
    /// Event that notifies subscribers about a weather change.
    /// </summary>
    public record WeatherChangedEvent(string Weather);
}
```

### Creating a Module

```csharp
using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Events;
using MicroserviceMonolith.Modules;

/// <summary>
/// This module listens for weather update requests and publishes weather change events.
/// It subscribes to the WeatherUpdateRequestEvent and triggers an asynchronous update.
/// </summary>
public class WeatherModule : ModuleBase
{
    private readonly IEventPublisher _eventPublisher;
    private IDisposable _subscription;

    /// <summary>
    /// Constructor that initializes the module with an event publisher.
    /// </summary>
    public WeatherModule(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// Initializes the module by subscribing to the WeatherUpdateRequestEvent.
    /// </summary>
    public override Task InitializeAsync()
    {
        _subscription = (_eventPublisher as IEventSubscriber)
            .Subscribe<ExampleApp.Events.WeatherUpdateRequestEvent>(HandleWeatherUpdateRequest);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles the weather update request event by triggering an update.
    /// </summary>
    private async Task HandleWeatherUpdateRequest(ExampleApp.Events.WeatherUpdateRequestEvent _)
    {
        await UpdateWeatherAsync();
    }

    /// <summary>
    /// Simulates weather retrieval and publishes an event with the new weather state.
    /// </summary>
    private async Task UpdateWeatherAsync()
    {
        string weather = "Sunny";
        
        // Publishing an event to notify other modules about the weather change.
        await _eventPublisher.PublishAsync(new ExampleApp.Events.WeatherChangedEvent(weather));
    }

    /// <summary>
    /// Shuts down the module by disposing of the subscription.
    /// </summary>
    public override Task ShutdownAsync()
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }
}
```

### Using the Module

```csharp
using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Events;
using MicroserviceMonolith.Logging;

class Program
{
    /// <summary>
    /// Entry point of the application. Initializes logging, event bus, and the weather module.
    /// Subscribes to weather change events and triggers a weather update request.
    /// Simulates user interaction by waiting for input before triggering an update.
    /// </summary>
    static async Task Main(string[] args)
    {
        // Create a logger instance
        ILogger logger = new ConsoleLogger();
        
        // Initialize the event bus and add a logging middleware
        var eventBus = new EventBus();
        eventBus.AddMiddleware(new LoggingEventMiddleware(logger));

        // Create and initialize the weather module
        var weatherModule = new WeatherModule(eventBus);
        await weatherModule.InitializeAsync();

        // Subscribe to weather change events to log the updates
        eventBus.Subscribe<ExampleApp.Events.WeatherChangedEvent>(e =>
        {
            logger.Log($"Weather updated: {e.Weather}");
            return Task.CompletedTask;
        });

        // Simulate user input before triggering a weather update
        Console.WriteLine("Press Enter to request a weather update...");
        Console.ReadLine();

        // Publish a weather update request event
        await eventBus.PublishAsync(new ExampleApp.Events.WeatherUpdateRequestEvent());
        
        // Wait for the event processing before shutting down
        await Task.Delay(1000);
        
        // Shut down the weather module
        await weatherModule.ShutdownAsync();
    }
}
```

### Run Example:

```sh
cd examples/ExampleApp
dotnet restore
dotnet build
dotnet run
```

## Contributing

1. Fork the repository.
2. Create a new branch.
3. Commit your changes.
4. Submit a pull request.

## License

This project is licensed under the MIT License. See the `LICENSE` file for details.

