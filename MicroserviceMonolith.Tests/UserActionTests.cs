using System;
using System.Threading.Tasks;
using FluentAssertions;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Adapters.UserActions;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class UserActionTests
    {
        [Fact]
        public async Task DispatchActionAsync_ExecutesRegisteredHandler()
        {
            var eventBus = new EventBus();
            var userAction = new UserAction(eventBus);

            string? receivedSender = null;
            int receivedVal = 0;

            userAction.RegisterAction("ClickButton", context =>
            {
                receivedSender = context.Sender.ToString();
                receivedVal = (int)context.Args;
                return Task.CompletedTask;
            });

            var ctx = new UserActionContext("SubmitBtn", 100);
            await userAction.DispatchActionAsync("ClickButton", ctx);

            receivedSender.Should().Be("SubmitBtn");
            receivedVal.Should().Be(100);
        }

        [Fact]
        public async Task DispatchActionAsync_UnregisteredAction_ThrowsArgumentException()
        {
            var eventBus = new EventBus();
            var userAction = new UserAction(eventBus);

            Func<Task> act = async () => await userAction.DispatchActionAsync("Unknown", new UserActionContext("btn", 1));

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Action 'Unknown' is not registered.*");
        }

        [Fact]
        public async Task ObsoleteCoreShim_StillWorks()
        {
#pragma warning disable CS0618
            var userAction = new MicroserviceMonolith.Core.UserActions.UserAction(new EventBus());
            string? sender = null;
            userAction.RegisterAction("Ping", ctx =>
            {
                sender = ctx.Sender.ToString();
                return Task.CompletedTask;
            });

            await userAction.DispatchActionAsync("Ping", new MicroserviceMonolith.Core.UserActions.UserActionContext("Btn", 1));
#pragma warning restore CS0618

            sender.Should().Be("Btn");
        }
    }
}
