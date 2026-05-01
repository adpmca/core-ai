namespace Diva.Tools.FileSystem.Abstractions;

public interface IPdfReader
{
    /// <summary>
    /// Extracts text (with page markers) and metadata from a PDF file.
    /// Returns error JSON string on encrypted or corrupt file — does NOT throw.
    /// </summary>
    string ExtractText(string filePath, PdfOptions opts);
}
