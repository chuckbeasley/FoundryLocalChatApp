using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using static FoundryLocalChatApp.Web.Services.ChatOptionsMapper;

namespace FoundryLocalChatApp.Web.Services
{
    public sealed class OpenAIChatClientAdapter : IChatClient
    {
        private readonly OpenAIChatClient _inner;
        private readonly ILogger? _logger;

        public OpenAIChatClientAdapter(OpenAIChatClient inner, ILogger logger)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _logger = logger;
        }

        public void Dispose()
        {
            if (_inner is IDisposable d)
            {
                d.Dispose();
            }
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages is null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

            var settings = ChatOptionsMapper.ToChatSettings(options);
            var ext = settings ?? new ExtendedChatSettings();
            ApplySettingsToInner(ext);

            // If tool-related values are set on the incoming ChatOptions, use direct core interop path to ensure they're included in the request.
            if (options?.ToolMode is not null || options?.AllowMultipleToolCalls is not null)
            {
                return await SendUsingCoreInteropAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }

            // Standard path
            var betalgoArray = CreateBetalgoMessagesArray(messages);
            var rawResponse = await _inner.CompleteChatAsync(betalgoArray, cancellationToken);

            return MapBetalgoToChatResponse(rawResponse);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages is null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

            var settings = ChatOptionsMapper.ToChatSettings(options);
            var ext = settings ?? new ExtendedChatSettings();
            ApplySettingsToInner(ext);

            // If tool-related values are set, use the core interop streaming callback path
            if (options?.ToolMode is not null || options?.AllowMultipleToolCalls is not null || options?.Tools is not null)
            {
                return StreamUsingCoreInterop(messages, options, cancellationToken);
            }

            // Standard provider streaming path
            var betalgoArray = CreateBetalgoMessagesArray(messages);
            IAsyncEnumerable<ChatCompletionCreateResponse> stream = _inner.CompleteChatStreamingAsync(betalgoArray, cancellationToken);
            return ConvertStream(stream, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType is null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (_inner is IChatClient ichat)
            {
                return ichat.GetService(serviceType, serviceKey);
            }

            try
            {
                var method = _inner.GetType().GetMethod("GetService", new[] { typeof(Type) });
                if (method != null)
                {
                    return method.Invoke(_inner, new object[] { serviceType });
                }
            }
            catch
            {
            }

            return null;
        }

        private void ApplySettingsToInner(ExtendedChatSettings settings)
        {
            if (settings is null)
            {
                return;
            }

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

        // Helper: serialize tools into a concrete DTO list. Only supports AIFunction instances (structured schema).
        private List<Tool> SerializeTools(IEnumerable<AITool> tools)
        {
            var list = new List<Tool>();
            if (tools == null)
            {
                return list;
            }

            foreach (var tool in tools)
            {
                if (tool == null)
                {
                    continue;
                }

                if (tool is AIFunction aiFunc)
                {
                    var functionDto = GetParameters(aiFunc);
                    var t = new Tool
                    {
                        Type = "function",
                        Function = functionDto
                    };

                    list.Add(t);
                }
                else
                {
                    // Non-AIFunction tools are not supported by this serializer; skip
                }
            }

            return list;
        }

        // Map the AIFunction's JSON schema into the local Function/Parameters model.
        // This replaces the previous dictionary-based mapping and produces a Function record
        // compatible with the project's `Tools` folder types.
        private Function GetParameters(AIFunction aiFunction)
        {
            if (aiFunction is null)
            {
                throw new ArgumentNullException(nameof(aiFunction));
            }

            JsonElement schema = aiFunction.JsonSchema;

            // Build required array
            string[] required = Array.Empty<string>();
            if (schema.TryGetProperty("required", out JsonElement requiredArray)
                && requiredArray.ValueKind == JsonValueKind.Array)
            {
                var reqList = new List<string>();
                foreach (var item in requiredArray.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        reqList.Add(s);
                    }
                }
                required = reqList.ToArray();
            }

            // Default parameters.type
            string paramType = "object";
            if (schema.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            {
                paramType = typeEl.GetString() ?? "object";
            }

            // Map properties into an IReadOnlyDictionary<string, Properties>
            var propsDict = new Dictionary<string, Properties>(StringComparer.OrdinalIgnoreCase);

            if (schema.TryGetProperty("properties", out JsonElement propertiesEl)
                && propertiesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in propertiesEl.EnumerateObject())
                {
                    var propName = prop.Name;
                    var propEl = prop.Value;

                    string propType = "string";
                    string propDescription = string.Empty;

                    if (propEl.ValueKind == JsonValueKind.Object)
                    {
                        if (propEl.TryGetProperty("type", out var locTypeEl) && locTypeEl.ValueKind == JsonValueKind.String)
                        {
                            propType = locTypeEl.GetString() ?? "string";
                        }

                        if (propEl.TryGetProperty("description", out var locDescEl) && locDescEl.ValueKind == JsonValueKind.String)
                        {
                            propDescription = locDescEl.GetString() ?? string.Empty;
                        }
                    }

                    var propModel = new Properties
                    {
                        Name = propName,
                        Description = propDescription
                    };

                    propsDict[propName] = propModel;
                }
            }

            var parameters = new Parameters
            {
                Type = paramType,
                Properties = new[] { propsDict },
                Required = required
            };

            var function = new Function
            {
                Name = aiFunction.Name ?? string.Empty,
                Description = aiFunction.Description ?? string.Empty,
                Parameters = parameters
            };

            return function;
        }

        private async IAsyncEnumerable<ChatResponseUpdate> StreamUsingCoreInterop(IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var core = FoundryInteropHelper.GetCoreInteropFromManagerSingleton() ?? FoundryInteropHelper.GetCoreInterop(_inner);
            if (core == null)
            {
                // fallback to provider streaming
                var betalgoArray = CreateBetalgoMessagesArray(messages);
                var stream = _inner.CompleteChatStreamingAsync(betalgoArray, cancellationToken);
                await foreach (var u in ConvertStream(stream, cancellationToken))
                {
                    yield return u;
                }

                yield break;
            }

            var modelId = FoundryInteropHelper.GetModelIdFromOpenAIChatClient(_inner);

            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = modelId
            };

            var msgs = new List<Dictionary<string, string?>>();
            foreach (var m in messages)
            {
                msgs.Add(new Dictionary<string, string?>
                {
                    ["role"] = m.Role.ToString().ToLowerInvariant(),
                    ["content"] = m.Text
                });
            }

            payload["messages"] = msgs;

            if (!string.IsNullOrEmpty(options?.ModelId))
            {
                payload["model"] = options!.ModelId;
            }

            if (options?.MaxOutputTokens is int mt)
            {
                payload["max_tokens"] = mt;
            }

            if (options?.Temperature is float t)
            {
                payload["temperature"] = t;
            }

            if (options?.TopP is float tp)
            {
                payload["top_p"] = tp;
            }

            if (options?.TopK is int tk)
            {
                payload["top_k"] = tk;
            }

            if (options?.FrequencyPenalty is float fp)
            {
                payload["frequency_penalty"] = fp;
            }

            if (options?.PresencePenalty is float pp)
            {
                payload["presence_penalty"] = pp;
            }

            if (options?.Seed is long s)
            {
                payload["seed"] = s;
            }

            if (options?.AllowMultipleToolCalls is not null)
            {
                payload["allow_multiple_tool_calls"] = options.AllowMultipleToolCalls.Value;
            }

            if (options?.Tools is not null)
            {
                // Serialize the tools metadata into a lightweight representation
                var toolsEnumerable = (IEnumerable<AITool>)options.Tools!;
                payload["tools"] = SerializeTools(toolsEnumerable);
            }

            if (options?.ToolMode is not null)
            {
                if (options.ToolMode == ChatToolMode.None)
                {
                    payload["tool_choice"] = "none";
                }
                else if (options.ToolMode == ChatToolMode.Auto)
                {
                    payload["tool_choice"] = "auto";
                }
            }

            string requestJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

            var coreReqObj = FoundryInteropHelper.CreateCoreInteropRequest(core, requestJson);
            if (coreReqObj == null)
            {
                var betalgoArray = CreateBetalgoMessagesArray(messages);
                var stream = _inner.CompleteChatStreamingAsync(betalgoArray, cancellationToken);
                await foreach (var u in ConvertStream(stream, cancellationToken))
                {
                    yield return u;
                }

                yield break;
            }

            var callbackStream = FoundryInteropHelper.ExecuteCommandWithCallbackAsStream(core, coreReqObj, "chat_completions", cancellationToken);

            await foreach (var callbackJson in callbackStream.WithCancellation(cancellationToken))
            {
                // callbackJson likely contains partial ChatCompletionCreateResponse JSON; try to deserialize
                ChatCompletionCreateResponse? berc = null;
                try
                {
                    berc = JsonSerializer.Deserialize<ChatCompletionCreateResponse>(callbackJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                }

                if (berc is not null)
                {
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

        // SendUsingCoreInteropAsync reused from previous changes; no reflection here for building JSON; minimal reflection used in helper.
        private async Task<ChatResponse> SendUsingCoreInteropAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
        {
            var core = FoundryInteropHelper.GetCoreInteropFromManagerSingleton() ?? FoundryInteropHelper.GetCoreInterop(_inner);
            if (core == null)
            {
                var betalgoArray = CreateBetalgoMessagesArray(messages);
                var rawResponse = await _inner.CompleteChatAsync(betalgoArray, cancellationToken);
                return MapBetalgoToChatResponse(rawResponse);
            }

            var modelId = FoundryInteropHelper.GetModelIdFromOpenAIChatClient(_inner);

            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = modelId
            };

            if (!string.IsNullOrEmpty(options?.ModelId))
            {
                payload["model"] = options!.ModelId;
            }

            if (options?.MaxOutputTokens is int mt)
            {
                payload["max_tokens"] = mt;
            }

            if (options?.Temperature is float t)
            {
                payload["temperature"] = t;
            }

            if (options?.TopP is float tp)
            {
                payload["top_p"] = tp;
            }

            if (options?.TopK is int tk)
            {
                payload["top_k"] = tk;
            }

            if (options?.FrequencyPenalty is float fp)
            {
                payload["frequency_penalty"] = fp;
            }

            if (options?.PresencePenalty is float pp)
            {
                payload["presence_penalty"] = pp;
            }

            if (options?.Seed is long s)
            {
                payload["seed"] = s;
            }

            if (options?.ToolMode is not null)
            {
                payload["tool_mode"] = options.ToolMode!.ToString();
            }

            if (options?.AllowMultipleToolCalls is not null)
            {
                payload["allow_multiple_tool_calls"] = options.AllowMultipleToolCalls.Value;
            }

            if (options?.Tools is not null)
            {
                payload["tools"] = SerializeTools(options.Tools!);
            }

            var msgs = new List<Dictionary<string, string?>>();
            foreach (var m in messages)
            {
                msgs.Add(new Dictionary<string, string?>
                {
                    ["role"] = m.Role.ToString().ToLowerInvariant(),
                    ["content"] = m.Text
                });
            }
            payload["messages"] = msgs;

            string requestJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

            var coreReqObj = FoundryInteropHelper.CreateCoreInteropRequest(core, requestJson);
            if (coreReqObj == null)
            {
                var betalgoArray = CreateBetalgoMessagesArray(messages);
                var rawResponse = await _inner.CompleteChatAsync(betalgoArray, cancellationToken);
                return MapBetalgoToChatResponse(rawResponse);
            }

            var resultJson = await FoundryInteropHelper.ExecuteCommandAsync(core, coreReqObj, "chat_completions", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(resultJson))
            {
                var betalgoArray = CreateBetalgoMessagesArray(messages);
                var rawResponse = await _inner.CompleteChatAsync(betalgoArray, cancellationToken);
                return MapBetalgoToChatResponse(rawResponse);
            }

            ChatCompletionCreateResponse? chatCompletion = null;
            try
            {
                chatCompletion = JsonSerializer.Deserialize<ChatCompletionCreateResponse>(resultJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }

            if (chatCompletion is null)
            {
                var betalgoArray = CreateBetalgoMessagesArray(messages);
                var rawResponse = await _inner.CompleteChatAsync(betalgoArray, cancellationToken);
                return MapBetalgoToChatResponse(rawResponse);
            }

            return MapBetalgoToChatResponse(chatCompletion);
        }

        private static ChatResponse MapBetalgoToChatResponse(ChatCompletionCreateResponse raw)
        {
            if (raw is null)
            {
                return new ChatResponse();
            }

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
            if (messages is null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

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
