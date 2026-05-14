namespace Diva.Core.Models;

public abstract class ContentPart { }

public sealed class TextContentPart : ContentPart
{
    public string Text { get; init; } = "";
}

/// <summary>Image content for vision-capable LLMs. Set Data (base64) OR Url — not both.</summary>
public sealed class ImageContentPart : ContentPart
{
    public string MediaType { get; init; } = "image/jpeg";
    public string? Data { get; init; }
    public string? Url { get; init; }
}

/// <summary>Document content (PDF, plain text, etc.) for document-understanding LLMs. Set Data OR Url.</summary>
public sealed class DocumentContentPart : ContentPart
{
    public string MediaType { get; init; } = "application/pdf";
    public string? Data { get; init; }
    public string? Url { get; init; }
    public string? Title { get; init; }
}
