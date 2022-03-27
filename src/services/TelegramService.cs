using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BuzzerBot
{
    public class TelegramService
    {
        private readonly ITelegramBotClient telegramBotClient;
        private readonly ILogger<TelegramService> logger;
        private readonly long chatId;
        private readonly string botUsername;

        private const string acceptNextCallCommand = "acceptnextcall";
        private const string generateAccessLink = "generateaccesslink";

        public TelegramService(ITelegramBotClient telegramBotClientParam, ILogger<TelegramService> loggerParam)
        {
            this.telegramBotClient = telegramBotClientParam;
            this.logger = loggerParam;
            this.chatId = long.Parse(Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID", EnvironmentVariableTarget.Process));
            this.botUsername = Environment.GetEnvironmentVariable("TELEGRAM_BOT_USERNAME", EnvironmentVariableTarget.Process);
        }

        public bool IsValid(Update update)
        {
            long? chatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id;
            return chatId == this.chatId;
        }

        public (BuzzerEvent, BuzzerEventPayload) GetBuzzerEventFromUpdate(Update update)
        {
            BuzzerEvent buzzerEvent = BuzzerEvent.NOOP;

            switch(update.Type)
            {
                case UpdateType.Message:
                    if (IsCommandType(update.Message, acceptNextCallCommand))
                    {
                         buzzerEvent = BuzzerEvent.SCHEDULE_APPROVAL;
                    }
                    else if(IsCommandType(update.Message, generateAccessLink))
                    {
                        buzzerEvent = BuzzerEvent.GENERATE_ACCESS_LINK;
                    }
                    return (buzzerEvent, null);

                case UpdateType.CallbackQuery:

                    var payload = new BuzzerEventPayload();
                    payload.TelegramUserId = update.CallbackQuery.From.Id;
                    payload.TelegramSenderName = GetUsername(update.CallbackQuery.From);


                    switch (update.CallbackQuery.Data)
                    {
                        case "approve":
                            buzzerEvent = BuzzerEvent.APPROVED;
                            return (buzzerEvent, payload);

                        case "reject":
                            buzzerEvent = BuzzerEvent.REJECTED;
                            return (buzzerEvent, payload);

                        default:
                            buzzerEvent = BuzzerEvent.NOOP;
                            return (buzzerEvent, null);
                    }

                default:
                    return (buzzerEvent, null);
            }
        }

        public async Task<int> SendChooseMessage()
        {
            InlineKeyboardMarkup inlineKeyboard = new(
                new[]
                {
                    new []
                    {
                        InlineKeyboardButton.WithCallbackData("Approve", "approve"),
                        InlineKeyboardButton.WithCallbackData("Reject", "reject"),
                    }
                }
            );

            Message message = await this.telegramBotClient.SendTextMessageAsync(
                chatId: this.chatId,
                text: "Choose",
                replyMarkup: inlineKeyboard
            );
            return message.MessageId;
        }

        public async Task SendAccessLink(string accessLink)
        {
            var reservedChars = "-.=";
            var scapedLink = accessLink;
            foreach (char c in reservedChars)
            {
                scapedLink = scapedLink.Replace($"{c}", $"\\{c}");
            }
            var message = $"The access link is [{scapedLink}]({accessLink})";
            await SendOrUpdate(message, null);
        }

        public async Task SendOrUpdateAcceptMessage(BuzzerEventPayload payload)
        {
            string message = "Request accepted";
            if (payload.TelegramUserId != null)
            {
                message = message + $" by [{payload.TelegramSenderName}](tg://user?id={payload.TelegramUserId.Value})";
            }

            await SendOrUpdate(message, payload.TelegramMessageId);
        }

        public async Task SendOrUpdateRejectMessage(BuzzerEventPayload payload)
        {
            string message = "Requested denied";
            if (payload.TelegramUserId != null)
            {
                message = message + $" by [{payload.TelegramSenderName}](tg://user?id={payload.TelegramUserId.Value})";
            }
            await SendOrUpdate(message, payload.TelegramMessageId);
        }

        public async Task SendOrUpdateTimeoutMessage(BuzzerEventPayload payload)
        {
            await SendOrUpdate("Request timed out", payload.TelegramMessageId);
        }

        public async Task SendOrUpdateHangUpMessage(BuzzerEventPayload payload)
        {
            await SendOrUpdate("The caller hung up", payload.TelegramMessageId);
        }

        public async Task SendOrUpdateErrorMessage(BuzzerEventPayload payload)
        {
            await SendOrUpdate("Request failed =[", payload.TelegramMessageId);
        }

        private async Task SendOrUpdate(string message, int? messageId)
        {
            if (messageId.HasValue == false)
            {
                await this.telegramBotClient.SendTextMessageAsync(chatId, message, ParseMode.MarkdownV2);
                return;
            }

            await this.telegramBotClient.EditMessageTextAsync(chatId, messageId.Value, message, ParseMode.MarkdownV2);
        }

        private bool IsCommandType(Message message, string command)
        {
            MessageEntity entity = message?.Entities?.ElementAt(0);
            if (entity?.Type == MessageEntityType.BotCommand)
            {
                string commandName = message.Text.Substring(1, entity.Length - 1);
                if (commandName.Equals(command, StringComparison.InvariantCultureIgnoreCase) ||
                    commandName.Equals($"{command}@{botUsername}", StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetUsername(User user)
        {
            if (user.Username != null)
            {
                return $"@{user.Username}";
            }

            return user.FirstName ?? $"@{user.Id}";
        }
    }
}
