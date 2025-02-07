using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Logging;
using MicroserviceMonolith.Core.UserActions;
using MicroserviceMonolith.Core.Modules;

// Для прикладу створимо простий модуль, який обробляє запити на оновлення "погоди"
// та публікує подію про зміну погоди.

namespace ExampleApp
{
    // Події для WeatherModule (розміщено в окремій збірці або просторі імен за бажанням)
    namespace ExampleApp.Events
    {
        public record WeatherUpdateRequestEvent();
        public record WeatherChangedEvent(string Weather);
    }

    // Простий модуль, який отримує запит на оновлення погоди
    // та публікує подію про зміну погоди.
    public class WeatherModule : ModuleBase
    {
        private readonly IEventPublisher _eventPublisher;
        private IDisposable _weatherRequestSubscription;

        public WeatherModule(IEventPublisher eventPublisher)
        {
            _eventPublisher = eventPublisher;
        }

        public override Task InitializeAsync()
        {
            // Підписка на запит на оновлення погоди (у реальному випадку можна використати EventSubscriber)
            _weatherRequestSubscription = (_eventPublisher as IEventSubscriber)
                .Subscribe<ExampleApp.Events.WeatherUpdateRequestEvent>(HandleWeatherUpdateRequest);
            return Task.CompletedTask;
        }

        private async Task HandleWeatherUpdateRequest(ExampleApp.Events.WeatherUpdateRequestEvent _)
        {
            Random rnd = new Random();
            for (int i = 0; i < 1; i++)
            {
                await UpdateWeatherAsync();
            }
        }

        private async Task UpdateWeatherAsync()
        {
            // Симуляція отримання даних (наприклад, запит до сервісу)
            string weather = "Сонячно";
            await _eventPublisher.PublishAsync(new ExampleApp.Events.WeatherChangedEvent(weather));
        }

        public IDisposable SubscribeToWeatherChanged(Func<ExampleApp.Events.WeatherChangedEvent, Task> handler)
        {
            return (_eventPublisher as IEventSubscriber).Subscribe(handler);
        }

        public override Task ShutdownAsync()
        {
            _weatherRequestSubscription?.Dispose();
            return Task.CompletedTask;
        }
    }
    class Program
    {
        static async Task Main(string[] args)
        {
            ILogger logger = new ConsoleLogger();

            // Налаштування EventBus із middleware (при потребі)
            var eventBus = new EventBus();
            eventBus.AddMiddleware(new ErrorHandlingEventMiddleware(logger));
            eventBus.AddMiddleware(new RetryEventMiddleware(3, logger));
            eventBus.AddMiddleware(new LoggingEventMiddleware(logger));

            // Створення та ініціалізація WeatherModule
            var weatherModule = new WeatherModule(eventBus);
            await weatherModule.InitializeAsync();
            weatherModule.SubscribeToWeatherChanged(async e =>
            {
                logger.Log($"Погода змінена: {e.Weather}");
                await Task.CompletedTask;
            });

            // Створення модуля для перетворення дій користувача у події
            var userActionsModule = new UserAction(eventBus);
            userActionsModule.RegisterAction("UpdateWeather", async context =>
            {
                logger.Log($"Дія UpdateWeather отримана. Тривалість: {context.Args} мс, джерело: {context.Sender}");
                await eventBus.PublishAsync(new ExampleApp.Events.WeatherUpdateRequestEvent());
            });

            for (int i = 0; i < 10; i++)
            {
                // Імітація дії користувача з параметрами
                var actionContext = new UserActionContext("Button1", 500);  
                await userActionsModule.DispatchActionAsync("UpdateWeather", actionContext);

                await Task.Delay(1000);
            }

            // Очікування для демонстрації роботи
            await Task.Delay(1000);
            //await weatherModule.ShutdownAsync();
        }
    }
}
