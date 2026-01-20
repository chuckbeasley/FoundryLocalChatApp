using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace FoundryLocalChatApp.Web.Services;

public class SemanticSearch(
    IEmbeddingGenerator<string, Embedding<float>> embeddingService,
    VectorStoreCollection<Guid, IngestedChunk> vectorCollection)
{
    public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
    {
        var embeddingResult = await embeddingService.GenerateAsync(text);
        var nearest = vectorCollection.SearchAsync(embeddingResult.Vector, maxResults, new VectorSearchOptions<IngestedChunk>
        {
            Filter = documentIdFilter is { Length: > 0 } ? record => record.DocumentId == documentIdFilter : null,
        });

        return await nearest.Select(result => result.Record).ToListAsync();
    }
}
