using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RabbitMq.Broker.Model;
using RabbitMq.Broker.Service.Interface;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitMq.Broker.Service.Implementation
{
    internal class MessageService : IMessageService
    {
        private List<MessageDetail> RegisteredListenMessages;
        private bool disposedValue;
        private RabbitConfiguration rabbitConfiguration;
        internal CancellationTokenSource cancellationTokenSource;
        private IRequestWatcher _requestWatcher;
        private IServiceProvider serviceProvider;


        public MessageService( List<MessageDetail> RegisteredListenMessages, IConfiguration configuration, IServiceProvider serviceProvider) 
        {
            this.RegisteredListenMessages = RegisteredListenMessages;
            rabbitConfiguration = new RabbitConfiguration()
            {
                ConnectionUrl = configuration["MessageBroker:ConnectionUrl"],
                RoutingTicket = configuration["MessageBroker:RoutingTicket"],
                ServiceKey = configuration["MessageBroker:ServiceKey"],
                UserName = configuration["MessageBroker:UserName"],
                Password = configuration["MessageBroker:Password"],
                HostName = configuration["MessageBroker:HostName"],
                Port = int.Parse(configuration["MessageBroker:Port"]),
                VirtualHost = "/"
            };
            cancellationTokenSource = new CancellationTokenSource();
            this.serviceProvider = serviceProvider;
            _requestWatcher = new RequestWatcher(RegisteredListenMessages, rabbitConfiguration, serviceProvider, cancellationTokenSource.Token);
        }

        public MessageService( IConfiguration configuration, IServiceProvider serviceProvider)
        {
            rabbitConfiguration = new RabbitConfiguration()
            {
                ConnectionUrl = configuration["MessageBroker:ConnectionUrl"],
                RoutingTicket = configuration["MessageBroker:RoutingTicket"],
                ServiceKey = configuration["MessageBroker:ServiceKey"],
                UserName = "TestRabbit",
                Password = "TestRabbit",
                HostName = "host.docker.internal",
                Port = 5672,
                VirtualHost = "/"
            };
            
            cancellationTokenSource = new CancellationTokenSource();
            this.serviceProvider = serviceProvider;
        }

        public void InvokeRequestListner()
        {
            Console.WriteLine("Starting Request Listener.");
            Console.WriteLine($"Rabbit Configuration : {JsonConvert.SerializeObject(rabbitConfiguration)}");
            InvokeListner();          
        }

        public void StopRequestListner()
        {
            Dispose(true);
        }

        public async Task<TResponse> SendRequest<TRequest, TResponse>(TRequest request) where TRequest : class where TResponse : class
        {
            Console.WriteLine($"Sending Request and response => Request : {JsonConvert.SerializeObject(request)}");
            IBrokerService brokerService = new BrokerService(rabbitConfiguration, serviceProvider, cancellationTokenSource.Token);
            TResponse response = await brokerService.SendRequest<TRequest, TResponse>(request).ConfigureAwait(false);
            Console.WriteLine($"Sending Request and response => Request : {JsonConvert.SerializeObject(response)}");
            brokerService.Dispose();
            return response;
        }

        private async void InvokeListner()
        {
            await Task.Run(() => _requestWatcher.ListenResponse(), cancellationTokenSource.Token).ConfigureAwait(false);
        }    

        #region Dispose

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {           
                    // TODO: dispose managed state (managed objects)
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Console.WriteLine("Ended Request Listener.");
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion Dispose

    }
}
