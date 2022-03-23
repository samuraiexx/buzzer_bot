using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BuzzerBot
{
    public class TelegramBot
    {
        private readonly ITelegramBotClient telegramBotClient;
        private readonly ILogger<TelegramBot> logger;
        private readonly long chatId;

        public TelegramBot(ITelegramBotClient telegramBotClientParam, ILogger<TelegramBot> loggerParam)
        {
            this.telegramBotClient = telegramBotClientParam;
            this.logger = loggerParam;

            this.chatId = long.Parse(Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID", EnvironmentVariableTarget.Process));
        }

        [FunctionName("TelegramBot")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request)
        {
            try
            {
                var body = await request.ReadAsStringAsync();
                var update = JsonConvert.DeserializeObject<Update>(body);

                switch(update.Type)
                {
                    case UpdateType.Message:
                        if (update.Message.Entities != null && update.Message.Entities.Length > 0)
                        {
                            var entity = update.Message.Entities[0];
                            if (entity.Type == MessageEntityType.BotCommand)
                            {
                                var command = update.Message.Text.Substring(0, entity.Length);
                            }
                        }
                        break;
                    case UpdateType.CallbackQuery:
                        await HandleCalbackQuery(update.CallbackQuery);
                        break;
                    default:
                        break;

                }
            }
            catch (Exception e)
            {
                logger.LogInformation("Exception: " + e.Message);
            }

            return new OkResult();
        }

        [FunctionName("TriggerBuzzer")]
        public async Task<IActionResult> RequestResponseForBuzzerCall(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request)
        {
            InlineKeyboardMarkup inlineKeyboard = new(
                new[]
                {
                    // first row
                    new []
                    {
                        InlineKeyboardButton.WithCallbackData("Open", "open"),
                        InlineKeyboardButton.WithCallbackData("Reject", "reject"),
                    }
                });

            await this.telegramBotClient.SendTextMessageAsync(
                chatId: this.chatId,
                text: "Choose",
                replyMarkup: inlineKeyboard
            );

            return new OkResult();
        }

        private async Task HandleCalbackQuery(CallbackQuery callbackQuery)
        {
            await this.telegramBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Processing...");
            switch(callbackQuery.Data)
            {
                case "open":
                    break;
            }
        }
    }
}
