using System;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.TwiML;

namespace BuzzerBot
{
    public class TwilioService
    {
        readonly string fallbackNumber;

        public TwilioService()
        {
            string accountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
            string authToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
            fallbackNumber = Environment.GetEnvironmentVariable("FALLBACK_NUMBER");

            TwilioClient.Init(accountSid, authToken);
        }

        public void SendRejectionMessage(string callSid)
        {
            VoiceResponse response = new VoiceResponse()
                .Say("Unfortunately your request was not approved.")
                .Hangup();

            CallResource.Update(
                method: Twilio.Http.HttpMethod.Post,
                twiml: response.ToString(),
                pathSid: callSid 
            );
        }

        public void SendOpenDoorSignal(string callSid)
        {
            VoiceResponse response = new VoiceResponse()
                .Play(digits: "ww6www")
                .Say("Unlocked!")
                .Hangup();

            CallResource.Update(
                method: Twilio.Http.HttpMethod.Post,
                twiml: response.ToString(),
                pathSid: callSid 
            );
        }

        public void SendApprovalFallback(string callSid)
        {
            VoiceResponse response = new VoiceResponse()
                .Dial(fallbackNumber);

            CallResource.Update(
                method: Twilio.Http.HttpMethod.Post,
                twiml: response.ToString(),
                pathSid: callSid 
            );
        }
    }
}
