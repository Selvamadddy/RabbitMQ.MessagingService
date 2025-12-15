using System;
using System.Threading.Tasks;

namespace RabbitMq.Broker.Service.Interface
{
    public interface IMessageService : IDisposable
    {
        void StopRequestListner();
        void InvokeRequestListner();
        Task<TResponse> SendRequest<TRequest, TResponse>(TRequest request) where TRequest : class where TResponse : class;
    }
}
