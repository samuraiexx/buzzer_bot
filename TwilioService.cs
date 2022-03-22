using System;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using System.IO;
using System.Threading.Tasks;

namespace BuzzerBot
{
    public class TwilioService
    {
        public TwilioService()
        {
            string accountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
            string authToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");

            TwilioClient.Init(accountSid, authToken);
        }

        public async Task Update(Stream pathIdStream)
        {
            string pathSid;
            using (StreamReader streamReader = new StreamReader(pathIdStream))
            {
                pathSid = await streamReader.ReadLineAsync();
            }

            CallResource.Update(
                method: Twilio.Http.HttpMethod.Post,
                twiml: new Twilio.Types.Twiml("<Response><Say>Ahoy there</Say></Response>"),
                pathSid: pathSid
            );
        }
    }
}
