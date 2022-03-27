using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace BuzzerBot
{
  public class BuzzerFunctions
  {
    private readonly TwilioService twilioService;
    private readonly TelegramService telegramService;
    private readonly ILogger<BuzzerFunctions> logger;

    public BuzzerFunctions(
        TelegramService telegramServiceParam,
        ILogger<BuzzerFunctions> loggerParam,
        TwilioService twilioServiceParam)
    {
      this.twilioService = twilioServiceParam;
      this.telegramService = telegramServiceParam;
      this.logger = loggerParam;
    }

    [FunctionName("ApprovalWorkflow")]
    public static async Task ApprovalWorkflow(
        [OrchestrationTrigger] IDurableOrchestrationContext context,
        ILogger logger)
    {
      string callSid = context.GetInput<string>();
      try
      {
        int? messageId = await context.CallActivityAsync<int?>(nameof(RequestBuzzerApproval), null);

        using var timeoutCts = new CancellationTokenSource();
        DateTime dueTime = context.CurrentUtcDateTime.AddSeconds(30);
        Task durableTimeout = context.CreateTimer(dueTime, timeoutCts.Token);

        Task<(BuzzerEvent, BuzzerEventPayload)> approvalEvent = context.WaitForExternalEvent<(BuzzerEvent, BuzzerEventPayload)>(BuzzerEventManager.BUZZER_EVENT);
        Task firedEvent = await Task.WhenAny(approvalEvent, durableTimeout);
        BuzzerEvent buzzerEvent;
        BuzzerEventPayload payload;

        if (firedEvent == approvalEvent)
        {
          timeoutCts.Cancel();
          (buzzerEvent, payload) = approvalEvent.Result;
        }
        else
        {
          buzzerEvent = BuzzerEvent.TIMEOUT;
          payload = new BuzzerEventPayload();
        }

        payload.TelegramMessageId = messageId;
        await context.CallActivityAsync(nameof(ProcessBuzzerEvent), (buzzerEvent, callSid, payload));
      }
      catch (Exception e)
      {
        await context.CallActivityAsync(nameof(ProcessBuzzerEvent), (BuzzerEvent.ERROR, callSid, new BuzzerEventPayload()));
        logger.LogError("[ApprovalWorkflow] Exception: " + e.Message);
      }
    }

    [FunctionName("ProcessBuzzerEvent")]
    public async Task ProcessBuzzerEvent([ActivityTrigger] IDurableActivityContext context)
    {
      logger.LogInformation("Processing Approval");
      (BuzzerEvent buzzerEvent, string callSid, BuzzerEventPayload payload) = context.GetInput<(BuzzerEvent, string, BuzzerEventPayload)>();

      switch (buzzerEvent)
      {
        case BuzzerEvent.APPROVED:
          twilioService.SendOpenDoorSignal(callSid);
          await telegramService.SendOrUpdateAcceptMessage(payload);
          break;
        case BuzzerEvent.REJECTED:
          twilioService.SendRejectionMessage(callSid);
          await telegramService.SendOrUpdateRejectMessage(payload);
          break;
        case BuzzerEvent.TIMEOUT:
          twilioService.SendApprovalRequestFallback(callSid);
          await telegramService.SendOrUpdateTimeoutMessage(payload);
          break;
        case BuzzerEvent.COMPLETED:
          await telegramService.SendOrUpdateHangUpMessage(payload);
          break;
        case BuzzerEvent.ERROR:
          twilioService.SendApprovalRequestFallback(callSid);
          await telegramService.SendOrUpdateErrorMessage(payload);
          break;
      }
    }

    [FunctionName("RequestBuzzerApproval")]
    public async Task<int?> RequestBuzzerApproval(
        [ActivityTrigger] IDurableActivityContext context,
        [Queue("acceptEntranceQueue")] QueueClient queueClient,
        [DurableClient] IDurableOrchestrationClient orchestrationClient)
    {
      var message = await queueClient.ReceiveMessageAsync();

      if (message.Value != null)
      {
        await queueClient.DeleteMessageAsync(message.Value.MessageId, message.Value.PopReceipt);
        await BuzzerEventManager.RaiseBuzzerEvent(orchestrationClient, BuzzerEvent.APPROVED, logger);
        return null;
      }

      return await telegramService.SendChooseMessage();
    }
  }
}
