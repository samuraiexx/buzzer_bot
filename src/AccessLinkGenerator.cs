using Azure.Storage.Queues;
using System;
using System.Threading.Tasks;

namespace BuzzerBot
{
    public class AccessLinkGenerator
    {
        private const string path = "/api/GetAccess";

        public static string GenerateAccessLink(QueueClient queueClient, string endpoint)
        {
            var messageId = queueClient.SendMessage("", timeToLive: TimeSpan.FromHours(24)).Value.MessageId;
            return $"{endpoint}{path}?token={messageId}";
        }
    }
}
