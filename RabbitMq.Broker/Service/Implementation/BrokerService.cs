using Newtonsoft.Json;
using RabbitMq.Broker.Model;
using RabbitMq.Broker.Service.Interface;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitMq.Broker.Service.Implementation
{
    public class BrokerService : IBrokerService
    {
        private bool disposedValue;
        private RabbitConfiguration rabbitConfiguration;
        private IServiceProvider serviceProvider;
        private CancellationToken cancellationToken;

        public BrokerService( RabbitConfiguration rabbitConfiguration, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            this.rabbitConfiguration = rabbitConfiguration;
            this.serviceProvider = serviceProvider;
            this.cancellationToken = cancellationToken;
        }

        //In memory sql for now, need to replace db later
        private Dictionary<string, string> serviceKeyDictionary = new Dictionary<string, string>();

        public async Task<TResponse> SendRequest<TRequest, TResponse>(TRequest request)
            where TRequest : class
            where TResponse : class
        {
            serviceKeyDictionary.Add("GetNameRequest", "BrokerTester");
            try
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                string requestQueueName = GenerateRequestQueueName(serviceKeyDictionary[request.GetType().Name]);
                string requestExchangeName = GenerateRequestExchangeName();
                string requestRoutingKeyName = GenerateRequestRoutingKey();

                var factory = new ConnectionFactory()
                {
                    UserName = rabbitConfiguration.UserName,
                    Password = rabbitConfiguration.Password,
                    HostName = rabbitConfiguration.HostName,
                    Port = rabbitConfiguration.Port,
                    VirtualHost = rabbitConfiguration.VirtualHost
                };

                using var connection = await factory.CreateConnectionAsync();
                using var channel = await connection.CreateChannelAsync();

                await channel.QueueDeclareAsync(queue: requestQueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

                await channel.ExchangeDeclareAsync(exchange: requestExchangeName, type: ExchangeType.Direct);

                await channel.QueueBindAsync(requestQueueName, requestExchangeName, requestRoutingKeyName);

                string correlationId = Guid.NewGuid().ToString();
                var props = new BasicProperties()
                {
                    CorrelationId = correlationId,
                    Headers = new Dictionary<string, object>
                    {
                        { "ClientServiceKey", rabbitConfiguration.ServiceKey }
                    },
                    ReplyTo = typeof(TResponse).FullName
                };
                RequestMessage requestMessage = new RequestMessage()
                {
                    MessageString = JsonConvert.SerializeObject(request),
                    MessageType = typeof(TRequest)
                };
                Console.WriteLine($"Sending Request with requestMessage: {JsonConvert.SerializeObject(requestMessage)}");
                var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestMessage));

                await channel.BasicPublishAsync(requestExchangeName, requestRoutingKeyName, true, props, body);

                await channel.QueueUnbindAsync(requestQueueName, requestExchangeName, requestRoutingKeyName);
                await channel.CloseAsync();
                await connection.CloseAsync();

                return await ListenResponse<TResponse>(correlationId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendRequest: {ex.Message}");
                throw;
            }
        }


        private async Task<TResponse> ListenResponse<TResponse>(string correlationId) where TResponse : class
        {
            try
            {
                string responseQueueName = GenerateResponseQueueName();

                var factory = new ConnectionFactory()
                {
                    UserName = rabbitConfiguration.UserName,
                    Password = rabbitConfiguration.Password,
                    HostName = rabbitConfiguration.HostName,
                    Port = rabbitConfiguration.Port,
                    VirtualHost = rabbitConfiguration.VirtualHost
                };

                using var connection = await factory.CreateConnectionAsync();
                using var channel = await connection.CreateChannelAsync();

                await channel.QueueDeclareAsync(
                    queue: responseQueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                var consumer = new AsyncEventingBasicConsumer(channel);

                var tcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

                consumer.ReceivedAsync += async (model, ea) =>
                {
                    try
                    {
                        Console.WriteLine($"Received message with CorrelationId: {ea.BasicProperties.CorrelationId}");
                        if (ea.BasicProperties.CorrelationId == correlationId)
                        {
                            var body = ea.Body.ToArray();
                            var message = Encoding.UTF8.GetString(body);

                            var responseMessage = JsonConvert.DeserializeObject<MessageResponse>(message);
                            var finalResponse = JsonConvert.DeserializeObject<TResponse>(
                                JsonConvert.SerializeObject(responseMessage.Response)
                            );

                            await channel.BasicAckAsync(ea.DeliveryTag, false);

                            // Safely complete the response task
                            tcs.TrySetResult(finalResponse);
                        }
                        else
                        {
                            // ---------- Your retry logic (unchanged) ----------
                            int retryCount = 0;
                            Console.WriteLine($"CorrelationId mismatch. Expected: {correlationId}, Received: {ea.BasicProperties.CorrelationId}. Initiating retry logic.");
                            var props = new BasicProperties
                            {
                                CorrelationId = ea.BasicProperties.CorrelationId,
                                Headers = ea.BasicProperties.Headers ?? new Dictionary<string, object>()
                            };

                            if (!props.Headers.ContainsKey("RetryCount"))
                                props.Headers.Add("RetryCount", 1);
                            else
                            {
                                retryCount = (int)props.Headers["RetryCount"];
                                props.Headers["RetryCount"] = retryCount + 1;
                            }
                            Console.WriteLine($"Current props: {JsonConvert.SerializeObject(props)}");
                            if (retryCount <= 5)
                            {
                                Console.WriteLine($"Retrying message. Current RetryCount: {retryCount}");
                                string retryExchangeName = GenerateRetryExchangeName();
                                string retryRoutingKeyName = GenerateRetryRoutingKey();

                                await channel.ExchangeDeclareAsync(retryExchangeName, ExchangeType.Direct);
                                await channel.QueueBindAsync(responseQueueName, retryExchangeName, retryRoutingKeyName);
                                Console.WriteLine("Publishing message to retry exchange.");
                                await channel.BasicPublishAsync(retryExchangeName, retryRoutingKeyName, true, props, ea.Body);
                                Console.WriteLine("Message published to retry exchange successfully.");
                                await channel.QueueUnbindAsync(responseQueueName, retryExchangeName, retryRoutingKeyName);
                            }
                            else {                                 
                                Console.WriteLine("Max retry attempts reached. Discarding message.");
                            }   
                            Console.WriteLine("Rejecting message without requeue.");
                            await channel.BasicRejectAsync(ea.DeliveryTag, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing received message: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                };

                await channel.BasicConsumeAsync(queue: responseQueueName, autoAck: false, consumer: consumer);

                // ---------- TIMEOUT HANDLING (2 minutes) ----------
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));

                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("Response timeout: no message received within 2 minutes.");
                    throw new TimeoutException("Response timeout: no message received within 2 minutes.");
                }

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ListenResponse: {ex.Message}");
                throw new ArgumentException("Error while processing message: " + ex.Message, ex);
            }
        }

        private string GenerateRequestQueueName(string serviceKey)
        {
            return $"{rabbitConfiguration.RoutingTicket}.{serviceKey}.Request";
        }

        private string GenerateRequestExchangeName()
        {
            return $"{rabbitConfiguration.ServiceKey}.Request";
        }

        private string GenerateRetryExchangeName()
        {
            return $"{rabbitConfiguration.ServiceKey}.RetryRequest";
        }

        private string GenerateRequestRoutingKey()
        {
            return $"{rabbitConfiguration.ServiceKey}.Request";
        }

        private string GenerateRetryRoutingKey()
        {
            return $"{rabbitConfiguration.ServiceKey}.RetryRequest";
        }

        private string GenerateResponseQueueName()
        {
            return $"{rabbitConfiguration.RoutingTicket}.{rabbitConfiguration.ServiceKey}.Response";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~BrokerService()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
