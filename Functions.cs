using System;
using System.Collections.Generic;
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
using Telegram.Bot.Types;

namespace BuzzerBot
{
    public class Functions
    {
        public const string APPROVAL_EVENT = "ApprovalEvent";
        private readonly TwilioService twilioService;
        private readonly TelegramService telegramService;
        private readonly ILogger<Functions> logger;

        public Functions(
            TelegramService telegramServiceParam,
            ILogger<Functions> loggerParam,
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
            if(!twilioService.ValidateRequest(request))
            {
                return new ForbidResult();
            }

            try
            {
                logger.LogInformation("Received a request!");
                string callSid = request.Form["CallSid"];
                await ClearDurableFunctions(orchestrationClient);
                await orchestrationClient.StartNewAsync(nameof(ApprovalWorkflow), null, callSid);

                return TwilioService.GetWaitRoomResponse(callSid);
            } catch(Exception e)
            {
                logger.LogError("[TwilioHttpTrigger] Exception: " + e.Message);
                return TwilioService.GetFallbackResponse();
            }
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

                BuzzerEvent action = telegramService.GetBuzzerEventFromUpdate(update);
                switch(action)
                {
                    case BuzzerEvent.APPROVED:
                    case BuzzerEvent.REJECTED:
                        await RaiseApprovalEvent(action, orchestrationClient);
                        break;
                    case BuzzerEvent.SCHEDULE_APPROVAL:
                        await queueClient.SendMessageAsync(DateTime.Now.ToString(), timeToLive: TimeSpan.FromMinutes(60));
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

                Task<BuzzerEvent> approvalEvent = context.WaitForExternalEvent<BuzzerEvent>(APPROVAL_EVENT);
                Task firedEvent = await Task.WhenAny(approvalEvent, durableTimeout);
                BuzzerEvent buzzerEvent;

                if (firedEvent == approvalEvent)
                {
                    timeoutCts.Cancel();
                    buzzerEvent = approvalEvent.Result;
                }
                else
                {
                    buzzerEvent = BuzzerEvent.TIMEOUT;
                }
                await context.CallActivityAsync(nameof(ProcessApproval), (buzzerEvent, callSid, messageId));
            } catch (Exception e)
            {
                await context.CallActivityAsync(nameof(ProcessApproval), (BuzzerEvent.ERROR, callSid, (int?)null));
                logger.LogError("[ApprovalWorkflow] Exception: " + e.Message);
            }
        }

        [FunctionName(nameof(ProcessApproval))]
        public async Task ProcessApproval([ActivityTrigger] IDurableActivityContext context)
        {
            logger.LogInformation("Processing Approval");
            (BuzzerEvent buzzerEvent, string callSid, int? messageId) = context.GetInput<(BuzzerEvent, string, int?)>();

            switch (buzzerEvent)
            {
                case BuzzerEvent.APPROVED:
                    twilioService.SendOpenDoorSignal(callSid);
                    await telegramService.SendOrUpdateAcceptMessage(messageId);
                    break;
                case BuzzerEvent.REJECTED:
                    twilioService.SendRejectionMessage(callSid);
                    await telegramService.SendOrUpdateRejectMessage(messageId);
                    break;
                case BuzzerEvent.TIMEOUT:
                    twilioService.SendApprovalRequestFallback(callSid);
                    await telegramService.SendOrUpdateTimeoutMessage(messageId);
                    break;
                case BuzzerEvent.ERROR:
                    twilioService.SendApprovalRequestFallback(callSid);
                    await telegramService.SendOrUpdateErrorMessage(messageId);
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
                await RaiseApprovalEvent(BuzzerEvent.APPROVED, orchestrationClient);
                return null;
            }

            return await telegramService.SendChooseMessage();
        }

        private async Task RaiseApprovalEvent(BuzzerEvent buzzerEvent, IDurableOrchestrationClient orchestrationClient)
        {
            OrchestrationStatusQueryResult result = await orchestrationClient.ListInstancesAsync(
                new OrchestrationStatusQueryCondition { RuntimeStatus = new [] {OrchestrationRuntimeStatus.Running} },
                CancellationToken.None
            );
            IEnumerable<DurableOrchestrationStatus> instances = result.DurableOrchestrationState;

            if (!instances.Any())
            {
                logger.LogCritical("No ongoing calls found to approve/reject");
                return;
            }

            string instanceId = instances.Single().InstanceId;
            await orchestrationClient.RaiseEventAsync(instanceId, Functions.APPROVAL_EVENT, buzzerEvent);
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
