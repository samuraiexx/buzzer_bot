# Buzzer

A complete system that consists of a Telegram Bot and a Twilio API that is able to handle Buzzer calls in a smart way.

## Local Setup

### Requirements

- Visual Studio 2022 (Azure Development .NET 6.0)
- Ngrok
- A [Telegram Bot](https://core.telegram.org/bots#6-botfather)
- A [Twilio Account](https://www.twilio.com/) with an active number

### Executing

1. Clone this repository
2. Create local.setting.json file with the code bellow and replace all empty strings

```
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "TWILIO_ACCOUNT_SID": "",
    "TWILIO_AUTH_TOKEN": "",
    "FALLBACK_NUMBER": "",
    "TELEGRAM_BOT_TOKEN": "",
    "TELEGRAM_CHAT_ID": ""
  }
}
```

3. Start the Azure Function on Visual Studio 2022 (BuzzerBot play button)
4. Create a ngrok server to redict external calls to your local server
5. Set your [Bot WebHook](https://core.telegram.org/bots/api#setwebhook) to point to your ngrok server (https://[ID].ngrok.io/api/TelegramHttpTrigger)
6. Set your Twilio number call webhook (https://[ID].ngrok.io/api/TwilioHttpTrigger) and status webhook (https://[ID].ngrok.io/api/TwilioStatusUpdateHttpTrigger) to your ngrok server
7. Call your twilio active number to test the workflow
