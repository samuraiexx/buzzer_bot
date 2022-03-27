using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace BuzzerBot
{
  public class TwilioFunctions
  {
    private readonly TwilioService twilioService;
    private readonly TelegramService telegramService;
    private readonly ILogger<TwilioFunctions> logger;

    public TwilioFunctions(
        TelegramService telegramServiceParam,
        ILogger<TwilioFunctions> loggerParam,
        TwilioService twilioServiceParam)
    {
      this.twilioService = twilioServiceParam;
      this.telegramService = telegramServiceParam;
      this.logger = loggerParam;
    }

    [FunctionName("TwilioHttpTrigger")]
    public async Task<IActionResult> TwilioHttpTrigger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request,
        [DurableClient] IDurableOrchestrationClient orchestrationClient)
    {
      try
      {
        if (!twilioService.IsRequestValid(request))
        {
          return new ForbidResult();
        }

        logger.LogInformation("Received a request!");
        string callSid = request.Form["CallSid"];
        await ClearDurableFunctions(orchestrationClient);
        await orchestrationClient.StartNewAsync(nameof(BuzzerFunctions.ApprovalWorkflow), null, callSid);

        return TwilioService.GetWaitRoomResponse();
      }
      catch (Exception e)
      {
        logger.LogError("[TwilioHttpTrigger] Exception: " + e.Message);
        return TwilioService.GetFallbackResponse();
      }
    }

    [FunctionName("TwilioStatusUpdateHttpTrigger")]
    public async Task<IActionResult> TwilioStatusUpdateHttpTrigger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request,
        [DurableClient] IDurableOrchestrationClient orchestrationClient)
    {
      try
      {
        if (!twilioService.IsRequestValid(request))
        {
          return new ForbidResult();
        }

        logger.LogInformation("Received a status update!");
        string body = new StreamReader(request.Body).ReadToEnd();

        if (twilioService.HasCallCompleted(body))
        {
          await BuzzerEventManager.RaiseBuzzerEvent(orchestrationClient, BuzzerEvent.COMPLETED, logger);
        }

        return new OkResult();
      }
      catch (Exception e)
      {
        logger.LogError("[TwilioStatusUpdateHttpTrigger] Exception: " + e.Message);
        throw;
      }
    }
    private async Task ClearDurableFunctions(IDurableOrchestrationClient client)
    {
      OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
          new OrchestrationStatusQueryCondition { RuntimeStatus = new[] { OrchestrationRuntimeStatus.Running } },
          CancellationToken.None
      );

      await Task.WhenAll(result
          .DurableOrchestrationState
          .Select(instance => client.TerminateAsync(instance.InstanceId, "Cleaning all functions"))
      );
    }
  }
}
