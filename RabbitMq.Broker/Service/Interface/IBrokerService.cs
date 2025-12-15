using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitMq.Broker.Service.Interface
{
    public interface IBrokerService : IDisposable
    {
        Task<TResponse> SendRequest<TRequest, TResponse>(TRequest request) where TRequest : class where TResponse : class;
    }
}
