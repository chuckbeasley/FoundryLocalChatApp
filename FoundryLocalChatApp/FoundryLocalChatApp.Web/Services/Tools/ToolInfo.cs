namespace FoundryLocalChatApp.Web.Services
{
    // Concrete DTO describing a tool exposed to the model.
    public record ToolInfo(
        string Name,
        string? Description,
        IReadOnlyDictionary<string, ToolParameter>? Parameters
    );
}
