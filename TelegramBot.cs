using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
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

        private const string acceptFirstCallInNextHourCommand = "acceptFirstCallInNextHour";

        public TelegramBot(ITelegramBotClient telegramBotClientParam, ILogger<TelegramBot> loggerParam)
        {
            this.telegramBotClient = telegramBotClientParam;
            this.logger = loggerParam;

            this.chatId = long.Parse(Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID", EnvironmentVariableTarget.Process));
        }

        [FunctionName("TelegramBot")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request,
            [Queue("acceptEntranceQueue")] QueueClient queueClient,
            [DurableClient] IDurableOrchestrationClient client)
        {
            try
            {
                var body = await request.ReadAsStringAsync();
                var update = JsonConvert.DeserializeObject<Update>(body);

                switch(update.Type)
                {
                    case UpdateType.Message:
                        await HandleMessage(update.Message, queueClient);
                        break;
                    case UpdateType.CallbackQuery:
                        await HandleCallbackQuery(update.CallbackQuery, client);
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

        [FunctionName("RequestBuzzerApproval")]
        public async Task<IActionResult> RequestBuzzerApproval(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request,
            [Queue("acceptEntranceQueue")] QueueClient queueClient,
            [DurableClient] IDurableOrchestrationClient client)
        {
            var message = queueClient.ReceiveMessage();

            if (message.Value != null)
            {
                queueClient.DeleteMessage(message.Value.MessageId, message.Value.PopReceipt);
                await UpdateRequest(true, client);
                await this.telegramBotClient.SendTextMessageAsync(chatId: this.chatId, "Request Accepted");

                return new OkResult();
            }

            InlineKeyboardMarkup inlineKeyboard = new(
                new[]
                {
                    // first row
                    new []
                    {
                        InlineKeyboardButton.WithCallbackData("Approve", "approve"),
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

        private async Task UpdateRequest(bool accepted, IDurableOrchestrationClient client)
        {
            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                new OrchestrationStatusQueryCondition { RuntimeStatus = new [] {OrchestrationRuntimeStatus.Running} },
                CancellationToken.None
            );

            string instanceId = result
                .DurableOrchestrationState
                .Single()
                .InstanceId;

            await client.RaiseEventAsync(instanceId, TwilioFunctions.APPROVAL_EVENT, accepted);
        }

        private async Task HandleCallbackQuery(CallbackQuery callbackQuery, IDurableOrchestrationClient client)
        {
            await this.telegramBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Processing...");
            switch(callbackQuery.Data)
            {
                case "approve":
                    await UpdateRequest(true, client);
                    await this.telegramBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Request accepted.");
                    break;
                case "reject":
                    await UpdateRequest(false, client);
                    await this.telegramBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, $"Request denied.");
                    break;
            }
        }

        private async Task HandleMessage(Message message, QueueClient queueClient)
        {
            if (message.Entities != null && message.Entities.Length > 0)
            {
                var entity = message.Entities[0];
                if (entity.Type == MessageEntityType.BotCommand)
                {
                    var commandName = message.Text.Substring(1, entity.Length - 1);

                    if (commandName.Equals(acceptFirstCallInNextHourCommand, StringComparison.InvariantCultureIgnoreCase))
                    {
                        await AcceptFirstCallForTimeInterval(60, queueClient);
                    }
                }
            }
        }

        private async Task AcceptFirstCallForTimeInterval(int timeIntervalInMinutes, QueueClient queueClient)
        {
            DateTime now = DateTime.Now;

            queueClient.SendMessage(DateTime.Now.ToString(), timeToLive: TimeSpan.FromMinutes(timeIntervalInMinutes));
        }
    }
}
