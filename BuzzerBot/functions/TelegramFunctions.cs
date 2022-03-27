using System;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Telegram.Bot.Types;

namespace BuzzerBot
{
  public class TelegramFunctions
  {
    private readonly TwilioService twilioService;
    private readonly TelegramService telegramService;
    private readonly ILogger<TelegramFunctions> logger;

    public TelegramFunctions(
        TelegramService telegramServiceParam,
        ILogger<TelegramFunctions> loggerParam,
        TwilioService twilioServiceParam)
    {
      this.twilioService = twilioServiceParam;
      this.telegramService = telegramServiceParam;
      this.logger = loggerParam;
    }

    [FunctionName("TelegramHttpTrigger")]
    public async Task<IActionResult> TelegramHttpTrigger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request,
        [Queue("acceptEntranceQueue")] QueueClient queueClient,
        [DurableClient] IDurableOrchestrationClient orchestrationClient)
    {
      try
      {
        string body = await request.ReadAsStringAsync();
        Update update = JsonConvert.DeserializeObject<Update>(body);

        if (!telegramService.IsValid(update))
        {
          return new OkResult();
        }

        (BuzzerEvent buzzerEvent, BuzzerEventPayload payload) = telegramService.GetBuzzerEventFromUpdate(update);

        switch (buzzerEvent)
        {
          case BuzzerEvent.APPROVED:
          case BuzzerEvent.REJECTED:
            await BuzzerEventManager.RaiseBuzzerEvent(orchestrationClient, buzzerEvent, logger, payload);
            break;
          case BuzzerEvent.SCHEDULE_APPROVAL:
            await QueueScheduledApproval(queueClient, update);
            break;
        }
      }
      catch (Exception e)
      {
        logger.LogError("[TelegramHttpTrigger] Exception: " + e.Message);
        throw;
      }

      return new OkResult();
    }

    private async Task QueueScheduledApproval(QueueClient queueClient, Update update)
    {
      int timeout;
      var args = update.Message.Text.Split(' ');

      if (args.Length < 2 || !int.TryParse(args[1], out timeout))
      {
        timeout = 1;
      }

      await queueClient.SendMessageAsync(DateTime.Now.ToString(), timeToLive: TimeSpan.FromHours(timeout));
    }
  }
}
