using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using RabbitMq.Broker.Model;
using RabbitMq.Broker.Model.Enum;
using RabbitMq.Broker.Service.Interface;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitMq.Broker.Service.Implementation
{
    internal class RequestWatcher : IRequestWatcher
    {
        private List<MessageDetail> RegisteredListenMessages;
        private RabbitConfiguration rabbitConfiguration;
        private IServiceProvider serviceProvider;
        private CancellationToken cancellationToken;
        private bool disposedValue;

        public RequestWatcher( List<MessageDetail> RegisteredListenMessages, RabbitConfiguration rabbitConfiguration, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            this.RegisteredListenMessages = RegisteredListenMessages;
            this.rabbitConfiguration = rabbitConfiguration;
            this.serviceProvider = serviceProvider;
            this.cancellationToken = cancellationToken;
        }

        public async Task ListenResponse()
        {
            await StartRabbitMqConsumer().ConfigureAwait(false);
        }

        #region Private Methods
        private async Task StartRabbitMqConsumer()
        {
            // Keep the listener alive and handle each message inside the consumer event handler.
            // Do not dispose connection/channel until cancellation is requested.

            string requestQueueName = GenerateRequestQueueName();

            var factory = new ConnectionFactory()
            {
                UserName = rabbitConfiguration.UserName,
                Password = rabbitConfiguration.Password,
                HostName = rabbitConfiguration.HostName,
                Port = rabbitConfiguration.Port,
                VirtualHost = rabbitConfiguration.VirtualHost
            };
            Console.WriteLine("Connecting to RabbitMQ...");
            IConnection responseConnection = null;
            IChannel responseChannel = null;

            try
            {
                responseConnection = await factory.CreateConnectionAsync().ConfigureAwait(false);
                responseChannel = await responseConnection.CreateChannelAsync().ConfigureAwait(false);
                Console.WriteLine("Connected to RabbitMQ.");
                await responseChannel.BasicQosAsync(0, 1, false);

                await responseChannel.QueueDeclareAsync(queue: requestQueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                Console.WriteLine($" [*] Waiting for messages in {requestQueueName}. To exit press CTRL+C");
                var consumer = new AsyncEventingBasicConsumer(responseChannel);
                consumer.ReceivedAsync += async (model, ea) =>
                {
                    try
                    {
                        byte[] body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);
                        Console.WriteLine($" [x] Received Request Message: {message}");
                        (MessageHandledStatus status, Object response) = await InvokeMessageHandler(message).ConfigureAwait(false);
                        Console.WriteLine($" [x] Processed Request Message with status: {status}");
                        if (status == MessageHandledStatus.Success)
                        {
                            await responseChannel.BasicAckAsync(ea.DeliveryTag, false).ConfigureAwait(false);
                            if (response != null)
                            {
                                string clientServiceKey = ea.BasicProperties.Headers?["ClientServiceKey"] != null ?
                                    Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers["ClientServiceKey"]) : null;
                                if (!string.IsNullOrEmpty(clientServiceKey))
                                {
                                    await SendResponse(clientServiceKey, ea.BasicProperties.CorrelationId, response).ConfigureAwait(false);
                                }
                            }
                        }
                        else if(status == MessageHandledStatus.Unregistered)
                        {
                            await responseChannel.BasicRejectAsync(ea.DeliveryTag, false).ConfigureAwait(false);
                        }
                        else if(status == MessageHandledStatus.Failed)
                        {
                            await responseChannel.BasicNackAsync(ea.DeliveryTag, false, false).ConfigureAwait(false);
                        }
                        Console.WriteLine(" [x] Done");
                    }
                    catch (Exception ex)
                    {
                        // Log and optionally requeue/nack
                        Console.WriteLine($"StartRabbitMqConsumer => Error processing message: {ex}");
                        try
                        {
                            // nack and requeue = true so message isn't lost; change policy as needed.
                            await responseChannel.BasicNackAsync(ea.DeliveryTag, false, true).ConfigureAwait(false);
                        }
                        catch
                        {
                            // swallow channel/ack errors to avoid crashing the consumer handler
                        }
                    }
                };

                // Start consuming (consumer will run until cancellation)

                string consumerTag = await responseChannel.BasicConsumeAsync(requestQueueName, autoAck: false, consumer: consumer).ConfigureAwait(false);

                // Keep method alive until cancellation is requested
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Listener initialization error: {ex}");
            }
            finally
            {
                // Dispose/close channel & connection gracefully
                try
                {
                    if (responseChannel != null && responseChannel.IsOpen)
                    {
                        try { await responseChannel.CloseAsync(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (responseConnection != null && responseConnection.IsOpen)
                    {
                        try { await responseConnection.CloseAsync(); } catch { }
                    }
                }
                catch { }

                try { responseChannel?.Dispose(); } catch { }
                try { responseConnection?.Dispose(); } catch { }
            }
        }

        private async Task<(MessageHandledStatus Status, object Response)> InvokeMessageHandler(string message)
        {
            try
            {
                (bool isdes, RequestMessage requestMessage) = TryDeserializeObject(message);
                if (!isdes) return (MessageHandledStatus.Unregistered, null);

                bool isRegistered = RegisteredListenMessages
                    .Any(x => x.MessageType.FullName == requestMessage.MessageType.FullName);

                if (!isRegistered)
                    return (MessageHandledStatus.Unregistered, null);

                var requestObj = JsonConvert.DeserializeObject(
                    requestMessage.MessageString,
                    requestMessage.MessageType);

                // Build generic handler type
                Type handlerGenericType = typeof(MessageHandler<>)
                    .MakeGenericType(requestMessage.MessageType);

                var handlerInstance = serviceProvider.GetRequiredService(handlerGenericType);

                // Provide value for message service property
                IBrokerService brokerService = new BrokerService(rabbitConfiguration, serviceProvider, cancellationToken);
                var prop = handlerGenericType.GetProperty("messageService");
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(handlerInstance, brokerService);
                }

                var method = handlerGenericType.GetMethod("OnRequestReceive");
                var returnType = method.ReturnType;

                // Call method
                var task = (Task)method.Invoke(handlerInstance, new object[] { requestObj });

                await task.ConfigureAwait(false);

                // -------- Handling async return types --------

                // Case 1: method returns Task (no result)
                if (returnType == typeof(Task))
                    return (MessageHandledStatus.Success, null);

                // Case 2: method returns Task<T>
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    // Get T from Task<T>
                    Type resultType = returnType.GenericTypeArguments[0];

                    // Get task.Result using reflection
                    object result = returnType.GetProperty("Result").GetValue(task);
                    Console.WriteLine($"Response Object : {JsonConvert.SerializeObject(result)}");
                    return (MessageHandledStatus.Success, result);
                }
                Console.WriteLine("Unknown return type from OnRequestReceive");
                return (MessageHandledStatus.Failed, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking message handler. {ex}");
                return (MessageHandledStatus.Failed, null);
            }
        }

        private (bool,RequestMessage) TryDeserializeObject(string message)
        {
            try
            {
                RequestMessage requestMessage = JsonConvert.DeserializeObject<RequestMessage>(message);
                return (true, requestMessage);
            }
            catch (Exception)
            {
                return (false,null);
            }
        }

        private async Task SendResponse<T>(string clientServiceKey, string correlationId, T response) where T : class, new()
        {
            try
            {
                string responseQueueName = GenerateResponseQueueName(clientServiceKey);
                string responseExchangeName = GenerateResponseExchangeName();
                string responseRoutingKeyName = GenerateResponseRoutingKey();

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

                await channel.QueueDeclareAsync(queue: responseQueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

                await channel.ExchangeDeclareAsync(exchange: responseExchangeName, type: ExchangeType.Direct);

                await channel.QueueBindAsync(responseQueueName, responseExchangeName, responseRoutingKeyName);

                var props = new BasicProperties()
                {
                    CorrelationId = correlationId,
                    Headers = new Dictionary<string, Object>()
                };
                props.Headers.Add("RetryCount", 0);

                var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response));

                await channel.BasicPublishAsync(responseExchangeName, responseRoutingKeyName, true, props, body);

                await channel.QueueUnbindAsync(responseQueueName, responseExchangeName, responseRoutingKeyName);
                await channel.CloseAsync();
                await connection.CloseAsync();

            }
            catch (Exception)
            {
            }
        }

        private string GenerateRequestQueueName()
        {
            return $"{rabbitConfiguration.RoutingTicket}.{rabbitConfiguration.ServiceKey}.Request";
        }

        private string GenerateResponseExchangeName()
        {
            return $"{rabbitConfiguration.ServiceKey}.Response";
        }

        private string GenerateResponseRoutingKey()
        {
            return $"{rabbitConfiguration.ServiceKey}.Response";
        }

        private string GenerateResponseQueueName(string clientServiceKey)
        {
            return $"{rabbitConfiguration.RoutingTicket}.{clientServiceKey}.Response";
        }
        #endregion Private Methods
        #region Dispose
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion Dispose
    }
}
