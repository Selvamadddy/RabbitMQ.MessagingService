using Microsoft.Extensions.Configuration;
using RabbitMq.Broker;
using RabbitMq.Broker.Model;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BrokerTester
{
    public class GetNameMessageHandler : MessageHandler<GetNameRequest>
    {
        private readonly IConfiguration _configuration;

        public GetNameMessageHandler(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        private Dictionary<int, string> _names = new Dictionary<int, string>();

        public override async Task<MessageResponse> OnRequestReceive(GetNameRequest getNameRequest)
        {
            _names.Add(1, "Grey");
            _names.Add(2, "Alice");

            if(!_names.ContainsKey(getNameRequest.Id))
            {
                return new MessageResponse() { IsSuccess = false, FailureReason = "Name not found", Response = null };
            }
            else
            {
                return new MessageResponse() { IsSuccess = true, FailureReason = null, Response = new GetNameResponse() { Name = _names[getNameRequest.Id] } };
            }
       
        }
    }
}
