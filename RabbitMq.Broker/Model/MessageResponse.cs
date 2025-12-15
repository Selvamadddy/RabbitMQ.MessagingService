namespace RabbitMq.Broker.Model
{
    public class MessageResponse 
    {
        public bool IsSuccess { get; set; }
        public string FailureReason { get; set; }
        public object Response { get; set; } 
    }
}