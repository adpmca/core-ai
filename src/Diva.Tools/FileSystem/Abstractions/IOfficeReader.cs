namespace Diva.Tools.FileSystem.Abstractions;

public interface IOfficeReader
{
    /// <summary>.docx → formatted text (headings as #/##/###, tables as Markdown pipe tables).
    /// summaryOnly=true returns heading outline only (~10x fewer tokens).
    /// startParagraph/maxParagraphs paginate through long documents.</summary>
    string ReadDocument(string filePath, OfficeOptions opts,
        bool summaryOnly = false, int startParagraph = 0, int maxParagraphs = 0);

    /// <summary>.xlsx → JSON { sheetCount, sheetsRead, sheets:[{name,rowCount,truncated,rows}] }.
    /// summaryOnly=true returns sheet names + column headers + row counts only (~50x fewer tokens).
    /// startRow/maxRows paginate within a sheet (overrides opts.MaxRowsPerSheet for this call).</summary>
    string ReadSpreadsheet(string filePath, OfficeOptions opts,
        bool summaryOnly = false, int startRow = 0, int maxRows = 0);

    /// <summary>.pptx → formatted text with "--- Slide N ---" markers and optional "[Notes: ...]" lines.
    /// summaryOnly=true returns slide titles only (~20x fewer tokens).
    /// startSlide/slideCount read a specific range of slides.</summary>
    string ReadPresentation(string filePath, OfficeOptions opts,
        bool summaryOnly = false, int startSlide = 0, int slideCount = 0);

    /// <summary>Search all paragraphs / cells / slides for text containing searchText.
    /// Returns compact JSON with location info. Use before replace_in_* to target exact text.</summary>
    string SearchInDocument(string filePath, string searchText, bool caseSensitive = false);
}
