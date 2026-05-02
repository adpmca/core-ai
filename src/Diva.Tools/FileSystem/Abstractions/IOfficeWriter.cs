namespace Diva.Tools.FileSystem.Abstractions;

public interface IOfficeWriter
{
    // ── Word ──────────────────────────────────────────────────────────────────

    /// <summary>Create or overwrite a .docx from Markdown-like content.
    /// "# " → Heading1, "## " → Heading2, "### " → Heading3.
    /// **bold**, *italic*, __underline__, ~~strikethrough~~, {color:RRGGBB}text{/color}, {size:N}text{/size}.
    /// Pipe-table blocks (lines starting with |) become Word tables.</summary>
    string WriteDocument(string filePath, string content);

    /// <summary>Append formatted content to the end of an existing .docx.
    /// Same Markdown-like syntax as WriteDocument.</summary>
    string AppendToDocument(string filePath, string content);

    /// <summary>Find and replace text in an existing .docx.
    /// maxReplacements=0 replaces all occurrences; 1 replaces only the first.</summary>
    string ReplaceInDocument(string filePath, string oldText, string newText, int maxReplacements = 0);

    // ── Excel ─────────────────────────────────────────────────────────────────

    /// <summary>Create or overwrite a .xlsx from a JSON array of arrays (rows × columns).
    /// First row is the header. Cell values starting with "=" are written as formulas.</summary>
    string WriteSpreadsheet(string filePath, string jsonRows);

    /// <summary>Update specific cells in an existing .xlsx.
    /// jsonCellUpdates: [{"sheet":"Sheet1","cell":"A1","value":"new"},...]
    /// Values starting with "=" are written as formulas.</summary>
    string UpdateCells(string filePath, string jsonCellUpdates);

    /// <summary>Compute a pivot summary table from a source sheet and write it to a new sheet.
    /// jsonPivotSpec: {"sourceSheet":"Sheet1","rowField":"Category","valueField":"Amount",
    ///   "aggregation":"sum"|"count"|"avg"|"min"|"max",
    ///   "columnField":"Region" (optional),"outputSheet":"PivotSummary"}.
    /// Produces a static cross-tab with row totals, column totals, and a grand total.</summary>
    string CreatePivotSummary(string filePath, string jsonPivotSpec);

    // ── PowerPoint ────────────────────────────────────────────────────────────

    /// <summary>Create a new .pptx from a JSON slide array.
    /// jsonSlides: [{"title":"Title","bullets":["Point","  - Sub-bullet"],"notes":"..."},...]
    /// Lines starting with "  - " or "    •" become indent-level-2 sub-bullets.</summary>
    string WritePresentation(string filePath, string jsonSlides);

    /// <summary>Append slides to an existing .pptx.
    /// jsonSlides: same format as WritePresentation.</summary>
    string AppendSlides(string filePath, string jsonSlides);

    /// <summary>Find and replace text across all slides (titles, body, notes) in an existing .pptx.
    /// maxReplacements=0 replaces all; 1 replaces only the first occurrence.</summary>
    string ReplaceInPresentation(string filePath, string oldText, string newText, int maxReplacements = 0);

    // ── PDF export ────────────────────────────────────────────────────────────

    /// <summary>Convert a .docx, .xlsx, or .pptx to PDF using LibreOffice headless.
    /// outputDirectory defaults to the same directory as the source file.
    /// Returns JSON {"pdfPath":"..."} on success, or an error object.</summary>
    string ConvertToPdf(string filePath, string? outputDirectory = null);
}
