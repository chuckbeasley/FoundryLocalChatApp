using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using static FoundryLocalChatApp.Web.Services.ChatOptionsMapper;

namespace FoundryLocalChatApp.Web.Services
{
    public sealed class OpenAIChatClientAdapter : IChatClient
    {
        private readonly OpenAIChatClient _inner;

        public OpenAIChatClientAdapter(OpenAIChatClient inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Dispose()
        {
            if (_inner is IDisposable d) d.Dispose();
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages is null) throw new ArgumentNullException(nameof(messages));

            var settings = ChatOptionsMapper.ToChatSettings(options);
            ApplySettingsToInner(settings);

            // Build Betalgo request message instances via reflection to avoid compile-time type mismatch
            var betalgoArray = CreateBetalgoMessagesArray(messages);

            var rawResponse = await _inner.CompleteChatAsync(betalgoArray, cancellationToken);


            return MapBetalgoToChatResponse(rawResponse);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages is null) throw new ArgumentNullException(nameof(messages));

            var settings = ChatOptionsMapper.ToChatSettings(options);
            ApplySettingsToInner(settings);

            var betalgoArray = CreateBetalgoMessagesArray(messages);
            IAsyncEnumerable<ChatCompletionCreateResponse> stream = _inner.CompleteChatStreamingAsync(betalgoArray, cancellationToken);
            return ConvertStream(stream, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));

            if (_inner is IChatClient ichat)
            {
                return ichat.GetService(serviceType, serviceKey);
            }

            try
            {
                var method = ((object)_inner).GetType().GetMethod("GetService", new[] { typeof(Type) });
                if (method != null)
                {
                    return method.Invoke((object)_inner, new object[] { serviceType });
                }
            }
            catch
            {
            }

            return null;
        }

        private void ApplySettingsToInner(ExtendedChatSettings settings)
        {
            if (settings is null) return;

            try
            {
                _inner.Settings.FrequencyPenalty = settings.FrequencyPenalty;
                _inner.Settings.MaxTokens = settings.MaxTokens;
                _inner.Settings.N = settings.N;
                _inner.Settings.Temperature = settings.Temperature;
                _inner.Settings.PresencePenalty = settings.PresencePenalty;
                _inner.Settings.RandomSeed = settings.RandomSeed;
                _inner.Settings.TopK = settings.TopK;
                _inner.Settings.TopP = settings.TopP;
                //_inner.Settings.ToolMode = settings.ToolMode;
                //_inner.Settings.AllowMultipleToolCalls = settings.AllowMultipleToolCalls;
            }
            catch
            {
            }
        }

        private static ChatResponse MapBetalgoToChatResponse(ChatCompletionCreateResponse raw)
        {
            if (raw is null) return new ChatResponse();

            var textParts = new List<string>();
            if (raw.Choices is { Count: > 0 } choices)
            {
                foreach (var choice in choices)
                {
                    try
                    {
                        var msg = choice.Message;
                        if (msg is not null)
                        {
                            var contentProp = msg.GetType().GetProperty("Content");
                            if (contentProp is not null)
                            {
                                var contentVal = contentProp.GetValue(msg);
                                if (contentVal is string s)
                                {
                                    textParts.Add(s);
                                    continue;
                                }
                            }

                            var asString = msg.ToString();
                            if (!string.IsNullOrEmpty(asString))
                            {
                                textParts.Add(asString);
                                continue;
                            }
                        }

                        var choiceText = choice.ToString();
                        if (!string.IsNullOrEmpty(choiceText))
                        {
                            textParts.Add(choiceText);
                        }
                    }
                    catch { }
                }
            }

            var text = string.Join(" ", textParts);
            if (string.IsNullOrEmpty(text))
            {
                text = raw.ToString() ?? string.Empty;
            }

            var message = new ChatMessage(ChatRole.Assistant, text);
            var response = new ChatResponse(message)
            {
                RawRepresentation = raw
            };

            return response;
        }

        private static IEnumerable<Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage> CreateBetalgoMessagesArray(IEnumerable<ChatMessage> messages)
        {
            if (messages is null) throw new ArgumentNullException(nameof(messages));

            var list = messages.ToList();

            // Create a strongly typed array of Betalgo request messages.
            // This avoids reflection and relies on the Betalgo request model being
            // available at compile-time: Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage
            var result = new Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage[list.Count];

            for (int i = 0; i < list.Count; i++)
            {
                var ms = list[i];

                // Construct the Betalgo request ChatMessage directly.
                // The typical Betalgo request model exposes string Content and string Role.
                // If the Betalgo model uses an enum for Role, update this assignment accordingly.
                var bm = new Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage
                {
                    Content = ms.Text,
                    Role = ms.Role.ToString().ToLowerInvariant()
                };

                result[i] = bm;
            }

            return result;
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> ConvertStream(IAsyncEnumerable<ChatCompletionCreateResponse> stream, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var berc in stream.WithCancellation(cancellationToken))
            {
                // Map betalgo incremental response (no reflection; use ToString fallbacks)
                var textParts = new List<string>();
                if (berc.Choices is { Count: > 0 } choices)
                {
                    foreach (var choice in choices)
                    {
                        try
                        {
                            var msg = choice.Message;
                            if (msg is not null)
                            {
                                var asString = msg.Content;
                                if (!string.IsNullOrEmpty(asString))
                                {
                                    textParts.Add(asString);
                                    continue;
                                }
                            }

                            var choiceText = choice.ToString();
                            if (!string.IsNullOrEmpty(choiceText))
                            {
                                textParts.Add(choiceText);
                            }
                        }
                        catch { }
                    }
                }

                var text = string.Join(" ", textParts);
                if (string.IsNullOrEmpty(text))
                {
                    text = berc.ToString() ?? string.Empty;
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, text) { RawRepresentation = berc };
            }
        }
    }
}
