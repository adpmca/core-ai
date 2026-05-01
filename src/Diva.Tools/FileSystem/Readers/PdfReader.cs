using Diva.Tools.FileSystem.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Diva.Tools.FileSystem.Readers;

public sealed class PdfReader(ILogger<PdfReader> logger) : IPdfReader
{
    public string ExtractText(string filePath, PdfOptions opts)
    {
        try
        {
            using var doc = PdfDocument.Open(filePath);
            var sb = new StringBuilder();

            if (opts.IncludeMetadata)
            {
                var info = doc.Information;
                sb.Append("[PDF:");
                if (!string.IsNullOrWhiteSpace(info.Title))   sb.Append($" Title: {info.Title} |");
                if (!string.IsNullOrWhiteSpace(info.Author))  sb.Append($" Author: {info.Author} |");
                sb.Append($" Pages: {doc.NumberOfPages}");
                if (info.CreationDate is not null) sb.Append($" | Created: {info.CreationDate}");
                sb.AppendLine("]");
                sb.AppendLine();
            }

            var pageLimit = Math.Min(doc.NumberOfPages, opts.MaxPages);
            for (var i = 1; i <= pageLimit; i++)
            {
                if (opts.IncludePageNumbers)
                    sb.AppendLine($"--- Page {i} ---");

                Page page = doc.GetPage(i);
                sb.AppendLine(page.Text);
            }

            if (doc.NumberOfPages > opts.MaxPages)
                sb.AppendLine($"[Truncated: showing {opts.MaxPages} of {doc.NumberOfPages} pages]");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            var fileName = Path.GetFileName(filePath);
            logger.LogWarning("PdfReader: failed to read '{File}': {Message}", fileName, ex.Message);
            return """{"error":"PdfReadError","message":"Could not read PDF: encrypted or invalid format"}""";
        }
    }
}
