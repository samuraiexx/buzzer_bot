using System;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

[assembly: FunctionsStartup(typeof(BuzzerBot.Startup))]

namespace BuzzerBot
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN", EnvironmentVariableTarget.Process);

            if (token == null)
            {
                throw new ArgumentException("Can not get token. Set token in environment setting");
            }

            builder.Services.AddLogging();
            builder.Services.AddScoped<TwilioService>();
            builder.Services
                .AddHttpClient("tgclient")
                .AddTypedClient<ITelegramBotClient>(httpClient => new TelegramBotClient(token, httpClient));
        }
    }
}
