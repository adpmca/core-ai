using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Diva.Tools.FileSystem.Abstractions;
using Microsoft.Extensions.Logging;
using A = DocumentFormat.OpenXml.Drawing;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using Paragraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using Table = DocumentFormat.OpenXml.Wordprocessing.Table;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace Diva.Tools.FileSystem.Readers;

public sealed class OfficeReader(ILogger<OfficeReader> logger) : IOfficeReader
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ── ReadDocument ───────────────────────────────────────────────────────

    public string ReadDocument(string filePath, OfficeOptions opts,
        bool summaryOnly = false, int startParagraph = 0, int maxParagraphs = 0)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var doc = WordprocessingDocument.Open(stream, isEditable: false);

            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("Document has no body.");

            var sb = new StringBuilder();

            if (opts.IncludeMetadata)
            {
                var props = doc.PackageProperties;
                if (!string.IsNullOrEmpty(props.Title))   sb.AppendLine($"[Title: {props.Title}]");
                if (!string.IsNullOrEmpty(props.Creator)) sb.AppendLine($"[Author: {props.Creator}]");
                if (props.Modified.HasValue)
                    sb.AppendLine($"[Modified: {props.Modified.Value:O}]");
                var extProps = doc.ExtendedFilePropertiesPart?.Properties;
                if (extProps?.Words?.Text is { } words)
                    sb.AppendLine($"[Words: {words}]");
                if (sb.Length > 0) sb.AppendLine();
            }

            // Collect top-level block elements (paragraphs + tables)
            var blocks = body.ChildElements
                .Where(e => e is Paragraph || e is Table)
                .ToList();

            if (summaryOnly)
            {
                // Heading outline only
                foreach (var block in blocks)
                {
                    if (block is not Paragraph para) continue;
                    var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                    var prefix = styleId switch
                    {
                        "Heading1" or "heading1" => "# ",
                        "Heading2" or "heading2" => "## ",
                        "Heading3" or "heading3" => "### ",
                        _ => null
                    };
                    if (prefix == null) continue;
                    sb.AppendLine(prefix + ParagraphText(para));
                }
                return sb.ToString();
            }

            // Paginate
            int end = maxParagraphs > 0 ? startParagraph + maxParagraphs : blocks.Count;
            end = Math.Min(end, blocks.Count);
            bool truncated = end < blocks.Count;

            for (int i = startParagraph; i < end; i++)
            {
                var block = blocks[i];

                if (block is Paragraph para)
                {
                    var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                    var prefix = styleId switch
                    {
                        "Heading1" or "heading1" => "# ",
                        "Heading2" or "heading2" => "## ",
                        "Heading3" or "heading3" => "### ",
                        _ => ""
                    };
                    var text = ParagraphText(para);
                    if (!string.IsNullOrWhiteSpace(text) || prefix.Length > 0)
                        sb.AppendLine(prefix + text);
                }
                else if (block is Table table && opts.IncludeWordTables)
                {
                    sb.AppendLine(RenderTableAsMarkdown(table));
                }
            }

            if (truncated)
                sb.AppendLine($"\n[Truncated: {blocks.Count - end} more blocks. Use startParagraph={end} to continue.]");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning("ReadDocument failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── ReadSpreadsheet ────────────────────────────────────────────────────

    public string ReadSpreadsheet(string filePath, OfficeOptions opts,
        bool summaryOnly = false, int startRow = 0, int maxRows = 0)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var wb = SpreadsheetDocument.Open(stream, isEditable: false);

            var wbPart = wb.WorkbookPart ?? throw new InvalidOperationException("Workbook has no parts.");
            var sheets = wbPart.Workbook.Sheets?.Elements<Sheet>().ToList() ?? [];

            // Build shared string table once
            var sst = wbPart.SharedStringTablePart?.SharedStringTable;
            var sharedStrings = new List<string>();
            if (sst is not null)
                foreach (var item in sst.Elements<SharedStringItem>())
                    sharedStrings.Add(item.InnerText);

            int sheetsToRead = Math.Min(sheets.Count, opts.MaxSheetsToRead);
            var sheetResults = new List<object>();

            foreach (var sheet in sheets.Take(sheetsToRead))
            {
                var sheetPart = wbPart.GetPartById(sheet.Id?.Value ?? "") as WorksheetPart;
                if (sheetPart is null) continue;

                var sheetData = sheetPart.Worksheet.GetFirstChild<SheetData>();
                var allRows = sheetData?.Elements<Row>().ToList() ?? [];

                if (summaryOnly)
                {
                    // Headers only (first data row) + row count
                    var headerRow = allRows.FirstOrDefault();
                    var headers = headerRow is null
                        ? Array.Empty<string>()
                        : headerRow.Elements<Cell>()
                            .Select(c => ResolveCellValue(c, sharedStrings))
                            .ToArray();
                    sheetResults.Add(new
                    {
                        name = sheet.Name?.Value ?? "",
                        rowCount = allRows.Count,
                        columnCount = headers.Length,
                        headers
                    });
                    continue;
                }

                int effectiveMax = maxRows > 0 ? maxRows : opts.MaxRowsPerSheet;
                int end = Math.Min(startRow + effectiveMax, allRows.Count);
                bool truncated = end < allRows.Count;

                var rows = new List<List<string>>();
                for (int i = startRow; i < end; i++)
                {
                    var rowCells = allRows[i].Elements<Cell>().ToList();
                    if (rowCells.Count == 0) continue;
                    var values = rowCells.Select(c => ResolveCellValue(c, sharedStrings)).ToList();
                    if (values.All(v => string.IsNullOrEmpty(v))) continue;
                    rows.Add(values);
                }

                sheetResults.Add(new
                {
                    name = sheet.Name?.Value ?? "",
                    rowCount = allRows.Count,
                    truncated,
                    rows
                });
            }

            var result = new
            {
                sheetCount = sheets.Count,
                sheetsRead = sheetsToRead,
                sheets = sheetResults
            };
            return JsonSerializer.Serialize(result, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ReadSpreadsheet failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── ReadPresentation ───────────────────────────────────────────────────

    public string ReadPresentation(string filePath, OfficeOptions opts,
        bool summaryOnly = false, int startSlide = 0, int slideCount = 0)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var prs = PresentationDocument.Open(stream, isEditable: false);

            var presPart = prs.PresentationPart ?? throw new InvalidOperationException("Presentation has no parts.");
            var slideIds = presPart.Presentation.SlideIdList?.Elements<SlideId>().ToList() ?? [];

            int effectiveStart = Math.Min(startSlide, slideIds.Count);
            int effectiveCount = slideCount > 0
                ? Math.Min(slideCount, opts.MaxSlidesToRead)
                : opts.MaxSlidesToRead;
            int end = Math.Min(effectiveStart + effectiveCount, slideIds.Count);

            var sb = new StringBuilder();
            sb.AppendLine($"[Slides: {slideIds.Count}]");
            sb.AppendLine();

            for (int i = effectiveStart; i < end; i++)
            {
                var slideId = slideIds[i];
                if (presPart.GetPartById(slideId.RelationshipId?.Value ?? "") is not SlidePart slidePart)
                    continue;

                sb.AppendLine($"--- Slide {i + 1} ---");

                if (summaryOnly)
                {
                    // Title only — first non-empty text from title placeholder
                    var title = ExtractSlideTitle(slidePart);
                    if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine(title);
                    continue;
                }

                // Full text from all shapes
                var slideText = slidePart.Slide.Descendants<A.Text>()
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                sb.AppendLine(string.Join("\n", slideText));

                if (opts.IncludeSpeakerNotes && slidePart.NotesSlidePart is { } notesPart)
                {
                    var notes = notesPart.NotesSlide.Descendants<A.Text>()
                        .Select(t => t.Text)
                        .Where(t => !string.IsNullOrWhiteSpace(t));
                    var notesText = string.Join(" ", notes).Trim();
                    if (!string.IsNullOrEmpty(notesText))
                        sb.AppendLine($"[Notes: {notesText}]");
                }

                sb.AppendLine();
            }

            if (end < slideIds.Count)
                sb.AppendLine($"[Truncated: {slideIds.Count - end} more slides. Use startSlide={end} to continue.]");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning("ReadPresentation failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── SearchInDocument ───────────────────────────────────────────────────

    public string SearchInDocument(string filePath, string searchText, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(searchText))
            return ErrorJson("IoError", "searchText must not be empty.");

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var matches = new List<object>();

            if (ext == ".docx")
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var doc = WordprocessingDocument.Open(stream, isEditable: false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body is null) return JsonSerializer.Serialize(matches, _json);

                int idx = 0;
                foreach (var block in body.ChildElements)
                {
                    string text = block is Paragraph p ? ParagraphText(p)
                        : block is Table t ? string.Concat(t.Descendants<Text>().Select(x => x.Text))
                        : "";
                    if (text.Contains(searchText, comparison))
                        matches.Add(new { type = "paragraph", index = idx, text = text.Length > 200 ? text[..200] + "…" : text });
                    idx++;
                }
            }
            else if (ext == ".xlsx")
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var wb = SpreadsheetDocument.Open(stream, isEditable: false);
                var wbPart = wb.WorkbookPart;
                if (wbPart is null) return JsonSerializer.Serialize(matches, _json);

                var sst = wbPart.SharedStringTablePart?.SharedStringTable;
                var sharedStrings = new List<string>();
                if (sst is not null)
                    foreach (var item in sst.Elements<SharedStringItem>())
                        sharedStrings.Add(item.InnerText);

                foreach (var sheet in wbPart.Workbook.Sheets?.Elements<Sheet>() ?? Enumerable.Empty<Sheet>())
                {
                    if (wbPart.GetPartById(sheet.Id?.Value ?? "") is not WorksheetPart sheetPart) continue;
                    foreach (var row in sheetPart.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>())
                    {
                        foreach (var cell in row.Elements<Cell>())
                        {
                            var val = ResolveCellValue(cell, sharedStrings);
                            if (val.Contains(searchText, comparison))
                                matches.Add(new { type = "cell", sheet = sheet.Name?.Value, cell = cell.CellReference?.Value, value = val });
                        }
                    }
                }
            }
            else if (ext == ".pptx")
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var prs = PresentationDocument.Open(stream, isEditable: false);
                var presPart = prs.PresentationPart;
                if (presPart is null) return JsonSerializer.Serialize(matches, _json);

                var slideIds = presPart.Presentation.SlideIdList?.Elements<SlideId>().ToList() ?? [];
                for (int i = 0; i < slideIds.Count; i++)
                {
                    if (presPart.GetPartById(slideIds[i].RelationshipId?.Value ?? "") is not SlidePart slidePart) continue;
                    var text = string.Concat(slidePart.Slide.Descendants<A.Text>().Select(t => t.Text));
                    if (text.Contains(searchText, comparison))
                        matches.Add(new { type = "slide", slideNumber = i + 1, text = text.Length > 200 ? text[..200] + "…" : text });
                }
            }
            else
            {
                return ErrorJson("IoError", $"Unsupported file type '{ext}' for search. Use .docx, .xlsx, or .pptx.");
            }

            return JsonSerializer.Serialize(new { matchCount = matches.Count, matches }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning("SearchInDocument failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string ParagraphText(Paragraph para) =>
        string.Concat(para.Descendants<Text>().Select(t => t.Text));

    private static string RenderTableAsMarkdown(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        bool first = true;
        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>()
                .Select(tc => string.Concat(tc.Descendants<Text>().Select(t => t.Text)).Replace("|", "\\|"))
                .ToList();
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
            if (first)
            {
                sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", cells.Count)));
                first = false;
            }
        }
        return sb.ToString();
    }

    private static string ResolveCellValue(Cell cell, List<string> sharedStrings)
    {
        var raw   = cell.CellValue?.Text ?? string.Empty;
        var dtype = cell.DataType?.Value;

        if (dtype == CellValues.SharedString && int.TryParse(raw, out var idx) && idx < sharedStrings.Count)
            return sharedStrings[idx];
        if (dtype == CellValues.Boolean)
            return raw == "1" ? "TRUE" : "FALSE";
        if (dtype == CellValues.Date &&
            double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return DateTime.FromOADate(d).ToString("O");
        if (dtype == CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? raw;
        if (dtype == CellValues.Error)
            return $"#ERR:{raw}";
        return raw;
    }

    private static int ColumnIndexFromRef(string cellRef)
    {
        var letters = new string(cellRef.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        return letters.Aggregate(0, (acc, c) => acc * 26 + (c - 'A' + 1)) - 1;
    }

    private static string ExtractSlideTitle(SlidePart slidePart)
    {
        // First text in the title placeholder (type="title" or idx="0")
        foreach (var sp in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Shape>())
        {
            var ph = sp.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties
                ?.GetFirstChild<PlaceholderShape>();
            if (ph?.Type?.Value == PlaceholderValues.Title ||
                (ph?.Index?.Value == 0 && ph?.Type is null))
            {
                var text = string.Concat(sp.Descendants<A.Text>().Select(t => t.Text)).Trim();
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        // Fallback: first non-empty text anywhere
        return string.Concat(slidePart.Slide.Descendants<A.Text>().Select(t => t.Text))
            .Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
    }

    private static string ErrorJson(string code, string message) =>
        JsonSerializer.Serialize(new { error = code, message }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
}
