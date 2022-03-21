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

namespace BuzzerBot
{
    public class TelegramBot
    {
        private readonly ITelegramBotClient telegramBotClient;
        private readonly ILogger<TelegramBot> logger;

        public TelegramBot(ITelegramBotClient telegramBotClientParam, ILogger<TelegramBot> loggerParam)
        {
            this.telegramBotClient = telegramBotClientParam;
            this.logger = loggerParam;
        }

        private async Task EchoAsync(Update update)
        {
            logger.LogInformation("Invoke telegram update function");

            if (update is null)
                return;

            if (update.Type == UpdateType.Message)
            {
                var message = update.Message;
                logger.LogInformation("Received Message from {0}", message.Chat.Id);
                await telegramBotClient.SendTextMessageAsync(message.Chat, $"Echo : {message.Text}");
            }
        }

        [FunctionName("TelegramBot")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request)
        {
            try
            {
                var body = await request.ReadAsStringAsync();
                var update = JsonConvert.DeserializeObject<Update>(body);

                await EchoAsync(update);
            }
            catch (Exception e)
            {
                logger.LogInformation("Exception: " + e.Message);
            }

            return new OkResult();
        }
    }
}
