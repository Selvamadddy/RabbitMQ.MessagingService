using RabbitMq.Broker.Model;
using RabbitMq.Broker.Service.Interface;
using System.Threading.Tasks;

namespace RabbitMq.Broker
{
    public abstract class MessageHandler<TRequest> where TRequest : class, new()
    {
        public IBrokerService messageService { get; set; }
        public abstract Task<MessageResponse> OnRequestReceive(TRequest request);
    }
}
