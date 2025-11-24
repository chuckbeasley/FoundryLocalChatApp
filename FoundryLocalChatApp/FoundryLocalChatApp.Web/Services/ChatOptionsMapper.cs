using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;

namespace FoundryLocalChatApp.Web.Services
{
    public static class ChatOptionsMapper
    {
        public record ExtendedChatSettings : OpenAIChatClient.ChatSettings
        {
            public ChatToolMode? ToolMode { get; set; }
            public bool? AllowMultipleToolCalls { get; set; }
            public IList<AITool>? Tools { get; set; }
        }

        public static ExtendedChatSettings ToChatSettings(ChatOptions? options)
        {
            var settings = new ExtendedChatSettings();
            if (options is null)
            {
                return settings;
            }

            if (options.FrequencyPenalty.HasValue)
            {
                settings.FrequencyPenalty = options.FrequencyPenalty.Value;
            }

            if (options.MaxOutputTokens.HasValue)
            {
                settings.MaxTokens = options.MaxOutputTokens.Value;
            }

            if (options.Temperature.HasValue)
            {
                settings.Temperature = options.Temperature.Value;
            }

            if (options.PresencePenalty.HasValue)
            {
                settings.PresencePenalty = options.PresencePenalty.Value;
            }

            if (options.Seed.HasValue)
            {
                try
                {
                    settings.RandomSeed = Convert.ToInt32(options.Seed.Value);
                }
                catch
                {
                    // ignore conversion errors; leave RandomSeed unset
                }
            }

            if (options.TopK.HasValue)
            {
                settings.TopK = options.TopK.Value;
            }

            if (options.TopP.HasValue)
            {
                settings.TopP = options.TopP.Value;
            }

            settings.ToolMode = options.ToolMode;
            settings.AllowMultipleToolCalls = options.AllowMultipleToolCalls;
            settings.Tools = options.Tools;
            return settings;
        }
    }
}
