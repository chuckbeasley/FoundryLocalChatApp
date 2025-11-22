using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;

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

            // Call inner dynamically
            dynamic dynInner = _inner;
            var raw = await dynInner.CompleteChatAsync(betalgoArray, cancellationToken);

            // If inner already returned Microsoft ChatResponse, return it
            if (raw is ChatResponse chatResp)
            {
                return chatResp;
            }

            // If inner returned Betalgo response, map it
            if (raw is ChatCompletionCreateResponse betalgo)
            {
                return MapBetalgoToChatResponse(betalgo);
            }

            // Fallback
            var text = raw?.ToString() ?? string.Empty;
            var message = new ChatMessage(ChatRole.Assistant, text);
            var response = new ChatResponse(message)
            {
                RawRepresentation = raw
            };

            return response;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages is null) throw new ArgumentNullException(nameof(messages));

            var settings = ChatOptionsMapper.ToChatSettings(options);
            ApplySettingsToInner(settings);

            var betalgoArray = CreateBetalgoMessagesArray(messages);

            dynamic dynInner = _inner;
            try
            {
                var stream = dynInner.CompleteChatStreamingAsync(betalgoArray, cancellationToken);
                return ConvertDynamicStream(stream, cancellationToken);
            }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                var stream = dynInner.CompleteChatStreamingAsync(betalgoArray);
                return ConvertDynamicStream(stream, cancellationToken);
            }
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

        private void ApplySettingsToInner(OpenAIChatClient.ChatSettings settings)
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
                        if (!string.IsNullOrEmpty(choiceText)) textParts.Add(choiceText);
                    }
                    catch { }
                }
            }

            var text = string.Join(string.Empty, textParts).Trim();
            if (string.IsNullOrEmpty(text)) text = raw.ToString() ?? string.Empty;

            var message = new ChatMessage(ChatRole.Assistant, text);
            var response = new ChatResponse(message)
            {
                RawRepresentation = raw
            };

            return response;
        }

        private static Array CreateBetalgoMessagesArray(IEnumerable<ChatMessage> messages)
        {
            // Create instances of the Betalgo request ChatMessage type at runtime
            var respAsm = typeof(ChatCompletionCreateResponse).Assembly;
            var requestType = respAsm.GetType("Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage");
            if (requestType is null)
            {
                // Fallback: return an empty object[] if type cannot be found
                return Array.Empty<object>();
            }

            var list = messages.ToList();
            var arr = Array.CreateInstance(requestType, list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                var ms = list[i];
                var inst = Activator.CreateInstance(requestType) ?? throw new InvalidOperationException("Unable to create Betalgo request message instance");

                // Try to set textual content properties
                foreach (var p in requestType.GetProperties())
                {
                    try
                    {
                        var name = p.Name.ToLowerInvariant();
                        if (p.PropertyType == typeof(string) && (name.Contains("content") || name.Contains("text") || name.Contains("message")))
                        {
                            p.SetValue(inst, ms.Text);
                            continue;
                        }

                        // Try to set role/author if available
                        if ((name == "role" || name == "author" || name == "authorname") )
                        {
                            if (p.PropertyType == typeof(string))
                            {
                                p.SetValue(inst, ms.Role.ToString().ToLowerInvariant());
                            }
                            else if (p.PropertyType.IsEnum)
                            {
                                try
                                {
                                    var enumVal = Enum.Parse(p.PropertyType, ms.Role.ToString(), true);
                                    p.SetValue(inst, enumVal);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                arr.SetValue(inst, i);
            }

            return arr;
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> ConvertDynamicStream(dynamic stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Use dynamic enumerator to iterate unknown IAsyncEnumerable at runtime
            var enumerator = ((object)stream).GetType().GetMethod("GetAsyncEnumerator", new[] { typeof(CancellationToken) })?.Invoke(stream, new object[] { cancellationToken });
            if (enumerator is null)
            {
                yield break;
            }

            try
            {
                while (true)
                {
                    // MoveNextAsync()
                    var moveNextMethod = enumerator.GetType().GetMethod("MoveNextAsync", Type.EmptyTypes);
                    if (moveNextMethod is null) yield break;

                    var moveTask = moveNextMethod.Invoke(enumerator, null);
                    // Await the returned ValueTask<bool>
                    var moveResult = await ((dynamic)moveTask);
                    if (!(moveResult is bool moved) || !moved) break;

                    // Get Current
                    var currentProp = enumerator.GetType().GetProperty("Current");
                    var current = currentProp?.GetValue(enumerator);

                    if (current is ChatCompletionCreateResponse berc)
                    {
                        // Map betalgo incremental response
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
                                        textParts.Add(msg.ToString() ?? string.Empty);
                                    }
                                    else
                                    {
                                        textParts.Add(choice.ToString());
                                    }
                                }
                                catch { }
                            }
                        }

                        var text = string.Join(string.Empty, textParts).Trim();
                        if (string.IsNullOrEmpty(text)) text = berc.ToString() ?? string.Empty;

                        yield return new ChatResponseUpdate(ChatRole.Assistant, text) { RawRepresentation = berc };
                    }
                    else if (current is ChatResponseUpdate cru)
                    {
                        yield return cru;
                    }
                    else
                    {
                        var txt = current?.ToString() ?? string.Empty;
                        yield return new ChatResponseUpdate(ChatRole.Assistant, txt) { RawRepresentation = current };
                    }
                }
            }
            finally
            {
                // Dispose async enumerator if possible
                var disposeAsync = enumerator.GetType().GetMethod("DisposeAsync", Type.EmptyTypes);
                if (disposeAsync is not null)
                {
                    var dispTask = disposeAsync.Invoke(enumerator, null);
                    if (dispTask is not null) await ((dynamic)dispTask);
                }
            }
        }
    }
}
