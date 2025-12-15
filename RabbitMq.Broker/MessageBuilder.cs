using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMq.Broker.Model;
using System.Collections.Generic;

namespace RabbitMq.Broker
{
    public class MessageBuilder
    {
        internal IConfiguration Configuration;
        internal IServiceCollection ServiceCollection;

        internal readonly List<MessageDetail> RegisteredListenMessages;

        public MessageBuilder(IConfiguration configuration, IServiceCollection serviceCollection) 
        {
            Configuration = configuration;
            ServiceCollection = serviceCollection;
            RegisteredListenMessages = new List<MessageDetail>();
        }

        public MessageBuilder(IServiceCollection serviceCollection) : this(null,serviceCollection) { }
        public MessageBuilder(IConfiguration configuration) : this(configuration, null) { }
    }
}
