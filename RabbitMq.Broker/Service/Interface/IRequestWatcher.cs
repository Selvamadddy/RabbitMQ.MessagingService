using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitMq.Broker.Service.Interface
{
    public interface IRequestWatcher : IDisposable
    {
        Task ListenResponse();
    }
}
