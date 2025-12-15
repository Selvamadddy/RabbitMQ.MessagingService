using BrokerTester;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMq.Broker;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BrokerTester1
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BrokerTester1 running at: {time}", DateTimeOffset.Now);
            while (!stoppingToken.IsCancellationRequested)
            {
                
                var name = await MessageConfiguration.SendRequest<GetNameRequest, GetNameResponse>(new GetNameRequest() { Id = 2});
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
