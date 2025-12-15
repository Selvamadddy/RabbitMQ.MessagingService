namespace RabbitMq.Broker.Model
{
    public class RabbitConfiguration
    {
        public string ConnectionUrl { get; set; }
        public string ServiceKey { get; set; }
        public string RoutingTicket { get; set; }

        public string UserName { get; set; }
        public string Password { get; set; }
        public string HostName { get; set; }
        public int Port { get; set; }
        public string VirtualHost { get; set; }
    }
}
