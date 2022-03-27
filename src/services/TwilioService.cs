using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Security;
using Twilio.TwiML;

namespace BuzzerBot
{
    public class TwilioService
    {
        private static readonly string fallbackNumber = Environment.GetEnvironmentVariable("FALLBACK_NUMBER");

        private readonly string authToken;
        private readonly IHostEnvironment hostEnvironment;

        public TwilioService(IHostEnvironment hostEnvironmentParam)
        {
            string accountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
            authToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
            this.hostEnvironment = hostEnvironmentParam;

            TwilioClient.Init(accountSid, authToken);
        }

        public bool IsRequestValid(HttpRequest request)
        {
            if(hostEnvironment.IsDevelopment())
            {
                return true;
            }

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

        public bool HasCallCompleted(string body)
        {
            NameValueCollection bodyCollection = HttpUtility.ParseQueryString(body);
            return bodyCollection.Get("CallStatus") == "completed";
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

        public void SendApprovalRequestFallback(string callSid)
        {
            VoiceResponse response = new VoiceResponse()
                .Dial(fallbackNumber);

            CallResource.Update(
                method: Twilio.Http.HttpMethod.Post,
                twiml: response.ToString(),
                pathSid: callSid 
            );
        }

        public static ContentResult GetFallbackResponse()
        {
            VoiceResponse response = new VoiceResponse()
                .Dial(fallbackNumber);

            return new ContentResult {
                Content = response.ToString(),
                StatusCode = StatusCodes.Status200OK,
                ContentType = "application/xml"
            };
        }

        public static ContentResult GetWaitRoomResponse()
        {
            VoiceResponse response = new VoiceResponse()
                .Play(new Uri("http://com.twilio.music.guitars.s3.amazonaws.com/Pitx_-_A_Thought.mp3"));
            // Alternative: http://com.twilio.music.rock.s3.amazonaws.com/nickleus_-_original_guitar_song_200907251723.mp3

            return new ContentResult {
                Content = response.ToString(),
                StatusCode = StatusCodes.Status200OK,
                ContentType = "application/xml"
            };
        }
    }
}
