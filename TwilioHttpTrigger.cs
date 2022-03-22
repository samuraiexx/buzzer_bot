using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Twilio.TwiML;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace BuzzerBot
{
    public class TwilioHttpTrigger
    {
        private readonly TwilioService twilioService;

        public TwilioHttpTrigger(TwilioService twilioServiceParam)
        {
            twilioService = twilioServiceParam;
        }

        [FunctionName("TwilioHttpTrigger")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            [Blob("files/pathSid", FileAccess.Write)] Stream pathSidStream,
            ILogger log)
        {
            log.LogInformation("Received a request!");
            IFormCollection formValues = req.Form;

            string number = formValues["From"]
                .ToString()
                .Replace("+", "")
                .Aggregate(string.Empty, (c, i) => c + i + ' ')
                .Trim();

            using (StreamWriter streamWriter = new StreamWriter(pathSidStream))
            {
                await streamWriter.WriteLineAsync(formValues["CallSid"]);
            }

            VoiceResponse response = new VoiceResponse()
                .Say($"Your phone number is {number}", loop: 20);

            string twiml = response.ToString().Replace("utf-16", "utf-8");
            return new ContentResult {
                Content = twiml,
                StatusCode = StatusCodes.Status200OK,
                ContentType = "application/xml"
            };
        }

        [FunctionName("Example")]
        public async Task<IActionResult> RunExample(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            [Blob("files/pathSid", FileAccess.Read)] Stream pathSidStream,
            ILogger log)
        {
            log.LogInformation("Executing example!");
            await twilioService.Update(pathSidStream);

            return new OkResult();
        }
    }
}
