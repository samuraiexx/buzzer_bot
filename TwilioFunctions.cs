using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Twilio.TwiML;

namespace BuzzerBot
{
    public class TwilioHttpTrigger
    {
        const string APPROVAL_EVENT = "ApprovalEvent";
        private readonly TwilioService twilioService;

        public TwilioHttpTrigger(TwilioService twilioServiceParam)
        {
            twilioService = twilioServiceParam;
        }

        [FunctionName("TwilioHttpTrigger")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            log.LogInformation("Received a request!");
            IFormCollection formValues = req.Form;

            VoiceResponse response = new VoiceResponse()
                .Play(new Uri("http://demo.twilio.com/docs/classic.mp3"));

            string callSid = formValues["CallSid"];
            string instanceId = await starter.StartNewAsync(nameof(ApprovalWorkflow), null, callSid);

            return new ContentResult {
                Content = response.ToString(),
                StatusCode = StatusCodes.Status200OK,
                ContentType = "application/xml"
            };
        }

        [FunctionName("ApprovalWorkflow")]
        public static async Task ApprovalWorkflow(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            string callSid = context.GetInput<string>();
            await context.CallActivityAsync(nameof(SendApprovalRequest), null);

            using (var timeoutCts = new CancellationTokenSource())
            {
                DateTime dueTime = context.CurrentUtcDateTime.AddSeconds(30);

                Task durableTimeout = context.CreateTimer(dueTime, timeoutCts.Token);
                Task<bool> approvalEvent = context.WaitForExternalEvent<bool>(APPROVAL_EVENT);

                Task firstEvent = await Task.WhenAny(approvalEvent, durableTimeout);
                if (firstEvent == approvalEvent)
                {
                    timeoutCts.Cancel();
                    await context.CallActivityAsync(nameof(ProcessApproval), (approvalEvent.Result, callSid));
                } else // tiemout
                {
                    await context.CallActivityAsync(nameof(ApprovalFallback), callSid);
                }
            }
        }

        [FunctionName(nameof(SendApprovalRequest))]
        public void SendApprovalRequest(
            [ActivityTrigger] IDurableActivityContext context,
            ILogger log)
        {
            log.LogInformation("Approval request sent");
        }

        [FunctionName(nameof(ProcessApproval))]
        public void ProcessApproval(
            [ActivityTrigger] IDurableActivityContext context,
            ILogger log)
        {
            log.LogInformation("ProcessApproval");
            (bool approved, string callSid) = context.GetInput<(bool, string)>();

            if(!approved)
            {
                twilioService.SendRejectionMessage(callSid);
                return;
            }
            twilioService.SendOpenDoorSignal(callSid);
        }

        [FunctionName(nameof(ApprovalFallback))]
        public void ApprovalFallback(
            [ActivityTrigger] IDurableActivityContext context,
            ILogger log)
        {
            log.LogInformation("ApprovalFallback");
            string callSid = context.GetInput<string>();
            twilioService.SendApprovalFallback(callSid);
        }

        [FunctionName(nameof(ApprovalExample))]
        public async Task ApprovalExample(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient client)
        {
            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                new OrchestrationStatusQueryCondition(),
                CancellationToken.None
            );

            string instanceId = result
                .DurableOrchestrationState
                .Where(instance => instance.RuntimeStatus == OrchestrationRuntimeStatus.Running)
                .Single()
                .InstanceId;

            await client.RaiseEventAsync(instanceId, APPROVAL_EVENT, true);
        }

        [FunctionName(nameof(RejectExample))]
        public async Task RejectExample(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient client)
        {
            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                new OrchestrationStatusQueryCondition(),
                CancellationToken.None
            );
            string instanceId = result
                .DurableOrchestrationState
                .Where(instance => instance.RuntimeStatus == OrchestrationRuntimeStatus.Running)
                .Single()
                .InstanceId;

            await client.RaiseEventAsync(instanceId, APPROVAL_EVENT, false);
        }

        [FunctionName(nameof(ClearDurableFunctions))]
        public async Task ClearDurableFunctions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient client)
        {
            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                new OrchestrationStatusQueryCondition(),
                CancellationToken.None
            );

            await Task.WhenAll(result
                .DurableOrchestrationState
                .Where(instance => instance.RuntimeStatus == OrchestrationRuntimeStatus.Running)
                .Select(instance => client.TerminateAsync(instance.InstanceId, "Cleaning all functions"))
            );
        }
    }
}
