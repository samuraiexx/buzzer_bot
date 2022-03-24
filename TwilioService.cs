using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Security;
using Twilio.TwiML;

namespace BuzzerBot
{
    public class TwilioService
    {
        private readonly string fallbackNumber;
        private readonly string authToken;

        public TwilioService()
        {
            string accountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
            authToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
            fallbackNumber = Environment.GetEnvironmentVariable("FALLBACK_NUMBER");

            TwilioClient.Init(accountSid, authToken);
        }

        public bool ValidateRequest(HttpRequest request)
        {
            RequestValidator validator = new RequestValidator(authToken);
            string signature = request.Headers["X-Twilio-Signature"];
            string url = UriHelper.GetDisplayUrl(request);
            Dictionary<string, string> body = new Dictionary<string, string>();

            foreach (var key in request.Form.Keys)
            {
                body.Add(key, request.Form[key]);
            }

            return validator.Validate(url, body, signature);
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
                .Pause()
                .Say("Unlocking...")
                .Play(digits: "www6ww")
                .Pause(2)
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
