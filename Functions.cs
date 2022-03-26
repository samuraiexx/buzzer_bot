using System;
using System.Collections.Generic;
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
using Telegram.Bot.Types;

namespace BuzzerBot
{
    public class Functions
    {
        public const string BUZZER_EVENT = "BuzzerEvent";
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
            try
            {
                if(!twilioService.IsRequestValid(request))
                {
                    return new ForbidResult();
                }

                logger.LogInformation("Received a request!");
                string callSid = request.Form["CallSid"];
                await ClearDurableFunctions(orchestrationClient);
                await orchestrationClient.StartNewAsync(nameof(ApprovalWorkflow), null, callSid);

                return TwilioService.GetWaitRoomResponse();
            } catch(Exception e)
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
                if(!twilioService.IsRequestValid(request))
                {
                    return new ForbidResult();
                }

                logger.LogInformation("Received a status update!");
                string body = new StreamReader(request.Body).ReadToEnd();

                if(twilioService.HasCallCompleted(body))
                {
                    await RaiseBuzzerEvent(orchestrationClient, BuzzerEvent.COMPLETED);
                }

                return new OkResult();
            } catch(Exception e)
            {
                logger.LogError("[TwilioStatusUpdateHttpTrigger] Exception: " + e.Message);
                throw;
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

                if(!telegramService.IsValid(update))
                {
                    return new ForbidResult();
                }

                (BuzzerEvent buzzerEvent, BuzzerEventPayload payload) = telegramService.GetBuzzerEventFromUpdate(update);

                switch(buzzerEvent)
                {
                    case BuzzerEvent.APPROVED:
                    case BuzzerEvent.REJECTED:
                        await RaiseBuzzerEvent(orchestrationClient, buzzerEvent, payload);
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

                Task<(BuzzerEvent, BuzzerEventPayload)> approvalEvent = context.WaitForExternalEvent<(BuzzerEvent, BuzzerEventPayload)>(BUZZER_EVENT);
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
            } catch (Exception e)
            {
                await context.CallActivityAsync(nameof(ProcessBuzzerEvent), (BuzzerEvent.ERROR, callSid, new BuzzerEventPayload()));
                logger.LogError("[ApprovalWorkflow] Exception: " + e.Message);
            }
        }

        [FunctionName(nameof(ProcessBuzzerEvent))]
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
                await RaiseBuzzerEvent(orchestrationClient, BuzzerEvent.APPROVED);
                return null;
            }

            return await telegramService.SendChooseMessage();
        }

        private async Task RaiseBuzzerEvent(
            IDurableOrchestrationClient orchestrationClient,
            BuzzerEvent buzzerEvent,
            BuzzerEventPayload payload = null)
        {
            OrchestrationStatusQueryResult result = await orchestrationClient.ListInstancesAsync(
                new OrchestrationStatusQueryCondition { RuntimeStatus = new [] {OrchestrationRuntimeStatus.Running} },
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

            await orchestrationClient.RaiseEventAsync(instanceId, Functions.BUZZER_EVENT, (buzzerEvent, payload));
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

        private async Task QueueScheduledApproval(QueueClient queueClient, Update update)
        {
            int timeout;

            var args = update.Message.Text.Split(' ');

            if (args.Length < 2 || !int.TryParse(args[1], out timeout)) timeout = 1;

            await queueClient.SendMessageAsync(DateTime.Now.ToString(), timeToLive: TimeSpan.FromHours(timeout));
        }
    }
}
