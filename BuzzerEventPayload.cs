namespace BuzzerBot
{
    public class BuzzerEventPayload
    {
        public int? TelegramMessageId { get; set; }
        public long? TelegramUserId { get; set; }
        public string TelegramSenderName { get; set; }
    }
}
