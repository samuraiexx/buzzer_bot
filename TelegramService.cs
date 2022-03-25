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

        private const string acceptFirstCallInNextHourCommand = "acceptFirstCallInNextHour";

        public TelegramService(ITelegramBotClient telegramBotClientParam, ILogger<TelegramService> loggerParam)
        {
            this.telegramBotClient = telegramBotClientParam;
            this.logger = loggerParam;
            this.chatId = long.Parse(Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID", EnvironmentVariableTarget.Process));
        }

        public bool IsValid(Update update)
        {
            long? chatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id;
            return chatId == this.chatId;
        }

        public BuzzerEvent GetBuzzerEventFromUpdate(Update update)
        {
            switch(update.Type)
            {
                case UpdateType.Message:
                    return IsScheduleApprovalCommand(update.Message) ?
                        BuzzerEvent.SCHEDULE_APPROVAL :
                        BuzzerEvent.NOOP;
                case UpdateType.CallbackQuery:
                    switch(update.CallbackQuery.Data)
                    {
                        case "approve":
                            return BuzzerEvent.APPROVED;
                        case "reject":
                            return BuzzerEvent.REJECTED;
                        default:
                            return BuzzerEvent.NOOP;
                    }
                default:
                    return BuzzerEvent.NOOP;
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

        public async Task SendOrUpdateAcceptMessage(int? messageId)
        {
            await SendOrUpdate("Request Accepted", messageId);
        }

        public async Task SendOrUpdateRejectMessage(int? messageId)
        {
            await SendOrUpdate("Request Denied", messageId);
        }

        public async Task SendOrUpdateTimeoutMessage(int? messageId)
        {
            await SendOrUpdate("Request Timed Out", messageId);
        }

        public async Task SendOrUpdateErrorMessage(int? messageId)
        {
            await SendOrUpdate("Request Failed =/", messageId);
        }

        private async Task SendOrUpdate(string message, int? messageId)
        {
            if (messageId.HasValue == false)
            {
                await this.telegramBotClient.SendTextMessageAsync(chatId, message);
                return;
            }

            await this.telegramBotClient.EditMessageTextAsync(chatId, messageId.Value, message);
        }

        private bool IsScheduleApprovalCommand(Message message)
        {
            MessageEntity entity = message?.Entities?.ElementAt(0);
            if (entity?.Type == MessageEntityType.BotCommand)
            {
                string commandName = message.Text.Substring(1, entity.Length - 1);
                if (commandName.Equals(acceptFirstCallInNextHourCommand, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
