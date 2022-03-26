# Buzzer
A complete system that consists of a Telegram Bot and a Twilio API that is able to handle Buzzer calls in a smart way.

## Local Setup

### Requirements
- Visual Studio 2022 (Azure Development .NET 6.0)
- Ngrok
- A [Telegram Bot](https://core.telegram.org/bots#6-botfather)
- A [Twilio Account](https://www.twilio.com/) with an active number

### Executing
- Clone this repository
- Edit local.setting.json and set all the empty variables
- Start the Azure Function on Visual Studio 2022 (BuzzerBot play button)
- Create a ngrok server to redict external calls to your local server
- Set your [Bot WebHook](https://core.telegram.org/bots/api#setwebhook) to point to your ngrok server (https://[ID].ngrok.io/api/TelegramHttpTrigger)
- Set your Twilio number call webhook (https://[ID].ngrok.io/api/TwilioHttpTrigger) and status webhook (https://[ID].ngrok.io/api/TwilioStatusUpdateHttpTrigger) to your ngrok server
- Call your twilio active number to test the workflow
