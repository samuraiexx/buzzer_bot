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
  public class BuzzerEventManager
  {
    public static string BUZZER_EVENT = "BuzzerEvent";

    public static async Task RaiseBuzzerEvent(
        IDurableOrchestrationClient orchestrationClient,
        BuzzerEvent buzzerEvent,
        ILogger logger,
        BuzzerEventPayload payload = null)
    {
      OrchestrationStatusQueryResult result = await orchestrationClient.ListInstancesAsync(
          new OrchestrationStatusQueryCondition { RuntimeStatus = new[] { OrchestrationRuntimeStatus.Running } },
          CancellationToken.None
      );
      IEnumerable<DurableOrchestrationStatus> instances = result.DurableOrchestrationState;

      if (!instances.Any())
      {
        logger.LogInformation("No ongoing calls found to raise an event");
        return;
      }

      payload ??= new BuzzerEventPayload();
      string instanceId = instances.Single().InstanceId;

      await orchestrationClient.RaiseEventAsync(instanceId, BuzzerEventManager.BUZZER_EVENT, (buzzerEvent, payload));
    }
  }
}
