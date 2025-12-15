using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMq.Broker.Model;
using RabbitMq.Broker.Service.Implementation;
using RabbitMq.Broker.Service.Interface;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RabbitMq.Broker
{
    public static class MessageConfiguration
    {
        public static MessageBuilder RegisterServiceCollection(this MessageBuilder messageBuilder, IServiceCollection serviceCollection)
        {
            messageBuilder.ServiceCollection = serviceCollection;
            return messageBuilder;
        }

        public static IServiceCollection ConfigureMessageBroker(this IServiceCollection serviceCollection, IConfiguration configuration, Action<MessageBuilder> builder)
        {
            if (serviceCollection == null)
            {
                throw new ArgumentNullException(nameof(ServiceCollection));
            }
            MessageBuilder messageBuilder = new MessageBuilder(configuration, serviceCollection);
            builder(messageBuilder);
            IMessageService messageService = messageBuilder.Build();
            serviceCollection.AddSingleton(messageService);
            messageService.InvokeRequestListner();
            return serviceCollection;
        }

        public static MessageBuilder RegisterListenMessage<TRequest, TMessageHandler>(this MessageBuilder builder) where TRequest : class, new() where TMessageHandler : MessageHandler<TRequest>
        {
            if (!builder.RegisteredListenMessages.Any(x => x.MessageType == typeof(TRequest)))
            {
                builder.RegisteredListenMessages.Add(GetMessageDetail<TRequest>());
            }
            bool flag = false;
            if(builder.ServiceCollection == null)
            {
                builder.ServiceCollection = new ServiceCollection();
                flag = true;
            }

            ConstructorInfo messageHandlerConstructorInfo = typeof(TMessageHandler).GetConstructor(Type.EmptyTypes);
            if (flag && messageHandlerConstructorInfo == null)
            {
                throw new ArgumentException("Error in messageconfiguration. Setup message handler properly");
            }
            builder.ServiceCollection.TryAddTransient<MessageHandler<TRequest>, TMessageHandler>();
            return builder;
        }

        private static IMessageService messageService;
        public static IMessageService Build(this MessageBuilder messageBuilder)
        {
            messageService = new MessageService( messageBuilder.RegisteredListenMessages, messageBuilder.Configuration, messageBuilder.ServiceCollection?.BuildServiceProvider());
            return messageService;
        }

        public static async Task<TResponse> SendRequest<TRequest, TResponse>(TRequest request) where TRequest : class where TResponse : class
        {
           TResponse response = await messageService.SendRequest<TRequest, TResponse>(request).ConfigureAwait(false);
            return response;
        }

        private static MessageDetail GetMessageDetail<T>() where T : class
        {
            Type type = typeof(T);
            return new MessageDetail() { MessageName = type.Name, MessageType = type };
        }
    }
}
