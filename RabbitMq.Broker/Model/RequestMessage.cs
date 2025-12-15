using System;

namespace RabbitMq.Broker.Model
{
    public class RequestMessage
    {
        public Type MessageType { get; set; }
        public string MessageString { get; set; }
    }
}