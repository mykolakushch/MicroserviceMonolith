using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Modules;

namespace MicroserviceMonolith.Core.UserActions
{
    // Модуль для універсального перетворення дій користувача у івенти
    public class UserAction : ModuleBase
    {
        private readonly IEventPublisher _eventPublisher;
        private readonly Dictionary<string, Func<UserActionContext, Task>> _actionMappings = new();

        public UserAction(IEventPublisher eventPublisher)
        {
            _eventPublisher = eventPublisher;
        }

        // Реєстрація дії користувача (actionName) з делегатом, який формує івент із контекстом дії
        public void RegisterAction(string actionName, Func<UserActionContext, Task> eventFactory)
        {
            _actionMappings[actionName] = eventFactory;
        }

        // Диспетчеризація дії користувача: перетворення її у івент із заданими параметрами
        public Task DispatchActionAsync(string actionName, UserActionContext context)
        {
            if (_actionMappings.TryGetValue(actionName, out var factory))
            {
                return factory(context);
            }
            throw new ArgumentException($"Дія '{actionName}' не зареєстрована");
        }
    }
}
