using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using OpenAIChatMessage = Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage;

namespace FoundryLocalChatApp.Web.Services;

/// <summary>
/// Adapter to wrap Microsoft.AI.Foundry.Local.OpenAIChatClient and implement Microsoft.Extensions.AI.IChatClient
/// </summary>
public sealed class OpenAIChatClientAdapter : IChatClient
{
    private readonly OpenAIChatClient _innerClient;

    public OpenAIChatClientAdapter(OpenAIChatClient innerClient)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
    }

    public ChatClientMetadata Metadata => new("phi-4-mini", providerUri: null);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Convert Microsoft.Extensions.AI.ChatMessage to Betalgo OpenAI ChatMessage
        var messages = chatMessages.Select(m => new OpenAIChatMessage
        {
            Role = m.Role.Value,
            Content = m.Text ?? string.Empty
        }).ToList();

        // Call the inner client's CompleteChatAsync method
        var response = await _innerClient.CompleteChatAsync(messages, cancellationToken);

        // Convert the response back to Microsoft.Extensions.AI.ChatResponse
        var responseMessage = new ChatMessage(ChatRole.Assistant, response?.Choices[0].Message.Content ?? string.Empty);
        return new ChatResponse([responseMessage]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Convert Microsoft.Extensions.AI.ChatMessage to Betalgo OpenAI ChatMessage
        var messages = chatMessages.Select(m => new OpenAIChatMessage
        {
            Role = m.Role.Value,
            Content = m.Text ?? string.Empty
        }).ToList();

        // Call the inner client's streaming method
        await foreach (var chunk in _innerClient.CompleteChatStreamingAsync(messages, cancellationToken))
        {
            var chunkText = chunk?.Choices[0].Message.Content ?? string.Empty;
            if (!string.IsNullOrEmpty(chunkText))
            {
                // Create a text content and add it to the update
                var textContent = new TextContent(chunkText);
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [textContent]
                };
            }
        }
    }

    public TService? GetService<TService>(object? key = null) where TService : class
    {
        if (typeof(TService) == typeof(OpenAIChatClient))
        {
            return _innerClient as TService;
        }

        return null;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(OpenAIChatClient))
        {
            return _innerClient;
        }

        return null;
    }

    public void Dispose()
    {
        // OpenAIChatClient may implement IDisposable, attempt to dispose if so
        if (_innerClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
