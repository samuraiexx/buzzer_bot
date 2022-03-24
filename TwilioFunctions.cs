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
    public class TwilioFunctions
    {
        public const string APPROVAL_EVENT = "ApprovalEvent";
        private readonly TwilioService twilioService;

        public TwilioFunctions(TwilioService twilioServiceParam)
        {
            twilioService = twilioServiceParam;
        }

        [FunctionName("TwilioHttpTrigger")]
        public async Task<IActionResult> TwilioHttpTrigger(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            if(!twilioService.ValidateRequest(request))
            {
                return new ForbidResult();
            }

            log.LogInformation("Received a request!");
            await ClearDurableFunctions(client);

            VoiceResponse response = new VoiceResponse()
                .Play(new Uri("http://com.twilio.music.guitars.s3.amazonaws.com/Pitx_-_A_Thought.mp3"));
            // Alternative: http://com.twilio.music.rock.s3.amazonaws.com/nickleus_-_original_guitar_song_200907251723.mp3

            string callSid = request.Form["CallSid"];
            string instanceId = await client.StartNewAsync(nameof(ApprovalWorkflow), null, callSid);

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
            await context.CallActivityAsync("RequestBuzzerApproval", null);

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
        private async Task ClearDurableFunctions(IDurableOrchestrationClient client)
        {
            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                new OrchestrationStatusQueryCondition { RuntimeStatus = new [] {OrchestrationRuntimeStatus.Running} },
                CancellationToken.None
            );

            await Task.WhenAll(result
                .DurableOrchestrationState
                .Select(instance => client.TerminateAsync(instance.InstanceId, "Cleaning all functions"))
            );
        }
    }
}
