using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMq.Broker;

namespace BrokerTester
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    #region RabbitMq Broker Configuration
                    services.ConfigureMessageBroker(hostContext.Configuration, x => x
                        .RegisterListenMessage<GetNameRequest, GetNameMessageHandler>()
                        .Build());
                    #endregion RabbitMq Broker Configuration
                    services.AddHostedService<Worker>();
                });
    }
}
