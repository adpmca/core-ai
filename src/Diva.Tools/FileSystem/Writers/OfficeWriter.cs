using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Diva.Tools.FileSystem.Abstractions;
using Microsoft.Extensions.Logging;
using A  = DocumentFormat.OpenXml.Drawing;
using SS = DocumentFormat.OpenXml.Spreadsheet;
using W  = DocumentFormat.OpenXml.Wordprocessing;

namespace Diva.Tools.FileSystem.Writers;

public sealed class OfficeWriter(ILogger<OfficeWriter> logger) : IOfficeWriter
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ── Word: WriteDocument ────────────────────────────────────────────────

    public string WriteDocument(string filePath, string content)
    {
        try
        {
            using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            var body = mainPart.Document.Body!;

            AppendContentToBody(body, content);
            body.AppendChild(new W.SectionProperties());
            mainPart.Document.Save();
            return "ok";
        }
        catch (Exception ex)
        {
            logger.LogWarning("WriteDocument failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── Word: AppendToDocument ─────────────────────────────────────────────

    public string AppendToDocument(string filePath, string content)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var doc = WordprocessingDocument.Open(stream, isEditable: true);
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("Document has no body.");

            var sectPr = body.Elements<W.SectionProperties>().LastOrDefault();
            AppendContentToBody(body, content, insertBefore: sectPr);
            doc.Save();
            return "ok";
        }
        catch (Exception ex)
        {
            logger.LogWarning("AppendToDocument failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── Word: ReplaceInDocument ────────────────────────────────────────────

    public string ReplaceInDocument(string filePath, string oldText, string newText, int maxReplacements = 0)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var doc = WordprocessingDocument.Open(stream, isEditable: true);
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("Document has no body.");

            int count = 0;
            foreach (var textElem in body.Descendants<W.Text>())
            {
                if (maxReplacements > 0 && count >= maxReplacements) break;
                if (textElem.Text.Contains(oldText, StringComparison.Ordinal))
                {
                    textElem.Text = textElem.Text.Replace(oldText, newText);
                    count++;
                }
            }

            doc.Save();
            return JsonSerializer.Serialize(new { replacements = count }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ReplaceInDocument failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── Excel: WriteSpreadsheet ────────────────────────────────────────────

    public string WriteSpreadsheet(string filePath, string jsonRows)
    {
        try
        {
            var rows = JsonSerializer.Deserialize<JsonArray>(jsonRows)
                ?? throw new ArgumentException("jsonRows must be a JSON array of arrays.");

            using var wb = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
            var wbPart = wb.AddWorkbookPart();
            wbPart.Workbook = new SS.Workbook();

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            wsPart.Worksheet = new SS.Worksheet(new SS.SheetData());

            var sheets = wbPart.Workbook.AppendChild(new SS.Sheets());
            sheets.Append(new SS.Sheet
            {
                Id = wbPart.GetIdOfPart(wsPart),
                SheetId = 1,
                Name = "Sheet1"
            });

            var sheetData = wsPart.Worksheet.GetFirstChild<SS.SheetData>()!;
            uint rowIndex = 1;
            foreach (var rowNode in rows)
            {
                var cells = rowNode?.AsArray() ?? [];
                var row = new SS.Row { RowIndex = rowIndex };
                int colIndex = 0;
                foreach (var cell in cells)
                    row.AppendChild(BuildCell(ColumnLetterFromIndex(colIndex++) + rowIndex, cell?.ToString() ?? ""));
                sheetData.AppendChild(row);
                rowIndex++;
            }

            wbPart.Workbook.Save();
            return "ok";
        }
        catch (Exception ex)
        {
            logger.LogWarning("WriteSpreadsheet failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── Excel: UpdateCells ─────────────────────────────────────────────────

    public string UpdateCells(string filePath, string jsonCellUpdates)
    {
        try
        {
            var updates = JsonSerializer.Deserialize<JsonArray>(jsonCellUpdates)
                ?? throw new ArgumentException("jsonCellUpdates must be a JSON array.");

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var wb = SpreadsheetDocument.Open(stream, isEditable: true);
            var wbPart = wb.WorkbookPart ?? throw new InvalidOperationException("Workbook has no parts.");

            foreach (var upd in updates)
            {
                var sheetName = upd?["sheet"]?.ToString() ?? "Sheet1";
                var cellRef   = upd?["cell"]?.ToString()  ?? throw new ArgumentException("Missing 'cell' field.");
                var value     = upd?["value"]?.ToString() ?? "";

                var sheet = wbPart.Workbook.Sheets?.Elements<SS.Sheet>()
                    .FirstOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Sheet '{sheetName}' not found.");

                var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id?.Value ?? "");
                var sheetData = wsPart.Worksheet.GetFirstChild<SS.SheetData>()!;

                uint rowIdx = uint.Parse(new string(cellRef.SkipWhile(char.IsLetter).ToArray()));
                var row = sheetData.Elements<SS.Row>().FirstOrDefault(r => r.RowIndex?.Value == rowIdx);
                if (row is null)
                {
                    row = new SS.Row { RowIndex = rowIdx };
                    sheetData.AppendChild(row);
                }

                var cell = row.Elements<SS.Cell>()
                    .FirstOrDefault(c => string.Equals(c.CellReference?.Value, cellRef, StringComparison.OrdinalIgnoreCase));
                if (cell is null)
                {
                    cell = new SS.Cell { CellReference = cellRef };
                    row.AppendChild(cell);
                }

                ApplyCellValue(cell, value);
            }

            wbPart.Workbook.Save();
            return "ok";
        }
        catch (Exception ex)
        {
            logger.LogWarning("UpdateCells failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── Excel: CreatePivotSummary ──────────────────────────────────────────

    public string CreatePivotSummary(string filePath, string jsonPivotSpec)
    {
        try
        {
            var spec = JsonSerializer.Deserialize<JsonObject>(jsonPivotSpec)
                ?? throw new ArgumentException("Invalid pivot spec JSON.");

            var sourceSheet  = spec["sourceSheet"]?.ToString() ?? "Sheet1";
            var rowField     = spec["rowField"]?.ToString()    ?? throw new ArgumentException("rowField required.");
            var valueField   = spec["valueField"]?.ToString()  ?? throw new ArgumentException("valueField required.");
            var aggregation  = spec["aggregation"]?.ToString() ?? "sum";
            var columnField  = spec["columnField"]?.ToString();
            var outputSheet  = spec["outputSheet"]?.ToString() ?? "PivotSummary";

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var wb = SpreadsheetDocument.Open(stream, isEditable: true);
            var wbPart = wb.WorkbookPart ?? throw new InvalidOperationException("Workbook has no parts.");

            var sst = wbPart.SharedStringTablePart?.SharedStringTable;
            var sharedStrings = new List<string>();
            if (sst is not null)
                foreach (var item in sst.Elements<SS.SharedStringItem>())
                    sharedStrings.Add(item.InnerText);

            var srcSheet = wbPart.Workbook.Sheets?.Elements<SS.Sheet>()
                .FirstOrDefault(s => string.Equals(s.Name?.Value, sourceSheet, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Source sheet '{sourceSheet}' not found.");

            var srcWsPart = (WorksheetPart)wbPart.GetPartById(srcSheet.Id?.Value ?? "");
            var srcRows = srcWsPart.Worksheet.GetFirstChild<SS.SheetData>()?.Elements<SS.Row>().ToList() ?? [];

            if (srcRows.Count < 2)
                return ErrorJson("IoError", "Source sheet has no data rows.");

            var headerCells = srcRows[0].Elements<SS.Cell>().ToList();
            var headers = headerCells.Select(c => ResolveCellValue(c, sharedStrings)).ToList();

            int rowFieldIdx  = headers.IndexOf(rowField);
            int valFieldIdx  = headers.IndexOf(valueField);
            int colFieldIdx  = columnField is null ? -1 : headers.IndexOf(columnField);

            if (rowFieldIdx < 0) return ErrorJson("IoError", $"rowField '{rowField}' not found in headers.");
            if (valFieldIdx < 0) return ErrorJson("IoError", $"valueField '{valueField}' not found in headers.");

            var dataRows = new List<Dictionary<string, string>>();
            foreach (var row in srcRows.Skip(1))
            {
                var cells = row.Elements<SS.Cell>().ToList();
                var dict = new Dictionary<string, string>();
                for (int i = 0; i < Math.Min(cells.Count, headers.Count); i++)
                    dict[headers[i]] = ResolveCellValue(cells[i], sharedStrings);
                dataRows.Add(dict);
            }

            var rowKeys = dataRows.Select(r => r.GetValueOrDefault(rowField, "")).Distinct().OrderBy(x => x).ToList();
            var colKeys = colFieldIdx >= 0
                ? dataRows.Select(r => r.GetValueOrDefault(columnField!, "")).Distinct().OrderBy(x => x).ToList()
                : new List<string>();

            double Aggregate(IEnumerable<double> vals) => aggregation.ToLowerInvariant() switch
            {
                "count" => vals.Count(),
                "avg"   => vals.Any() ? vals.Average() : 0,
                "min"   => vals.Any() ? vals.Min() : 0,
                "max"   => vals.Any() ? vals.Max() : 0,
                _       => vals.Sum()
            };

            // Build or replace output sheet
            var existingOut = wbPart.Workbook.Sheets?.Elements<SS.Sheet>()
                .FirstOrDefault(s => string.Equals(s.Name?.Value, outputSheet, StringComparison.OrdinalIgnoreCase));
            WorksheetPart outWsPart;
            if (existingOut is not null)
            {
                outWsPart = (WorksheetPart)wbPart.GetPartById(existingOut.Id?.Value ?? "");
                outWsPart.Worksheet = new SS.Worksheet(new SS.SheetData());
            }
            else
            {
                outWsPart = wbPart.AddNewPart<WorksheetPart>();
                outWsPart.Worksheet = new SS.Worksheet(new SS.SheetData());
                var sheetId = (wbPart.Workbook.Sheets?.Elements<SS.Sheet>().Max(s => s.SheetId?.Value) ?? 0) + 1;
                wbPart.Workbook.Sheets!.Append(new SS.Sheet
                {
                    Id = wbPart.GetIdOfPart(outWsPart),
                    SheetId = sheetId,
                    Name = outputSheet
                });
            }

            var outData = outWsPart.Worksheet.GetFirstChild<SS.SheetData>()!;
            uint rowNum = 1;

            var headerRow = new SS.Row { RowIndex = rowNum };
            headerRow.AppendChild(BuildCell("A" + rowNum, rowField));
            int startCol = 1;
            foreach (var ck in colKeys)
                headerRow.AppendChild(BuildCell(ColumnLetterFromIndex(startCol++) + rowNum, ck));
            headerRow.AppendChild(BuildCell(ColumnLetterFromIndex(startCol) + rowNum,
                colKeys.Count > 0 ? "Total" : valueField));
            outData.AppendChild(headerRow);
            rowNum++;

            double[] colTotals = new double[colKeys.Count + 1];

            foreach (var rk in rowKeys)
            {
                var dataRow = new SS.Row { RowIndex = rowNum };
                dataRow.AppendChild(BuildCell("A" + rowNum, rk));
                int col = 1;
                double rowTotal = 0;
                if (colKeys.Count > 0)
                {
                    foreach (var ck in colKeys)
                    {
                        var vals = dataRows
                            .Where(r => r.GetValueOrDefault(rowField, "") == rk && r.GetValueOrDefault(columnField!, "") == ck)
                            .Select(r => double.TryParse(r.GetValueOrDefault(valueField, ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0)
                            .ToList();
                        double agg = Aggregate(vals);
                        colTotals[col - 1] += agg;
                        rowTotal += agg;
                        dataRow.AppendChild(BuildCell(ColumnLetterFromIndex(col++) + rowNum, FormatNum(agg)));
                    }
                    colTotals[col - 1] += rowTotal;
                    dataRow.AppendChild(BuildCell(ColumnLetterFromIndex(col) + rowNum, FormatNum(rowTotal)));
                }
                else
                {
                    var vals = dataRows
                        .Where(r => r.GetValueOrDefault(rowField, "") == rk)
                        .Select(r => double.TryParse(r.GetValueOrDefault(valueField, ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0)
                        .ToList();
                    double agg = Aggregate(vals);
                    colTotals[0] += agg;
                    dataRow.AppendChild(BuildCell(ColumnLetterFromIndex(1) + rowNum, FormatNum(agg)));
                }
                outData.AppendChild(dataRow);
                rowNum++;
            }

            var totalsRow = new SS.Row { RowIndex = rowNum };
            totalsRow.AppendChild(BuildCell("A" + rowNum, "Total"));
            for (int ci = 0; ci <= colKeys.Count; ci++)
                totalsRow.AppendChild(BuildCell(ColumnLetterFromIndex(ci + 1) + rowNum, FormatNum(colTotals[ci])));
            outData.AppendChild(totalsRow);

            wbPart.Workbook.Save();
            return JsonSerializer.Serialize(new { outputSheet, rowCount = rowKeys.Count, colCount = colKeys.Count }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning("CreatePivotSummary failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── PowerPoint: WritePresentation ──────────────────────────────────────

    public string WritePresentation(string filePath, string jsonSlides)
    {
        try
        {
            var slides = ParseSlideSpec(jsonSlides);
            using var prs = PresentationDocument.Create(filePath, PresentationDocumentType.Presentation);
            var presPart = prs.AddPresentationPart();
            presPart.Presentation = new Presentation(
                new SlideIdList(),
                new SlideSize { Cx = 9144000, Cy = 5143500 },
                new NotesSize  { Cx = 6858000, Cy = 9144000 });

            uint slideId = 256;
            foreach (var slide in slides)
            {
                var slidePart = presPart.AddNewPart<SlidePart>();
                BuildSlide(slidePart, slide);
                presPart.Presentation.SlideIdList!.Append(new SlideId
                {
                    Id = slideId++,
                    RelationshipId = presPart.GetIdOfPart(slidePart)
                });
            }

            presPart.Presentation.Save();
            return "ok";
        }
        catch (Exception ex)
        {
            logger.LogWarning("WritePresentation failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── PowerPoint: AppendSlides ───────────────────────────────────────────

    public string AppendSlides(string filePath, string jsonSlides)
    {
        try
        {
            var slides = ParseSlideSpec(jsonSlides);
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var prs = PresentationDocument.Open(stream, isEditable: true);
            var presPart = prs.PresentationPart ?? throw new InvalidOperationException("Presentation has no parts.");

            uint maxId = presPart.Presentation.SlideIdList?
                .Elements<SlideId>().Max(s => s.Id?.Value) ?? 255;
            uint slideId = maxId + 1;

            foreach (var slide in slides)
            {
                var slidePart = presPart.AddNewPart<SlidePart>();
                BuildSlide(slidePart, slide);
                presPart.Presentation.SlideIdList!.Append(new SlideId
                {
                    Id = slideId++,
                    RelationshipId = presPart.GetIdOfPart(slidePart)
                });
            }

            presPart.Presentation.Save();
            return "ok";
        }
        catch (Exception ex)
        {
            logger.LogWarning("AppendSlides failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── PowerPoint: ReplaceInPresentation ─────────────────────────────────

    public string ReplaceInPresentation(string filePath, string oldText, string newText, int maxReplacements = 0)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var prs = PresentationDocument.Open(stream, isEditable: true);
            var presPart = prs.PresentationPart ?? throw new InvalidOperationException("Presentation has no parts.");

            int count = 0;
            var slideIds = presPart.Presentation.SlideIdList?.Elements<SlideId>().ToList() ?? [];

            foreach (var slideId in slideIds)
            {
                if (maxReplacements > 0 && count >= maxReplacements) break;
                if (presPart.GetPartById(slideId.RelationshipId?.Value ?? "") is not SlidePart slidePart) continue;

                foreach (var textElem in slidePart.Slide.Descendants<A.Text>())
                {
                    if (maxReplacements > 0 && count >= maxReplacements) break;
                    if (textElem.Text.Contains(oldText, StringComparison.Ordinal))
                    {
                        textElem.Text = textElem.Text.Replace(oldText, newText);
                        count++;
                    }
                }

                if (slidePart.NotesSlidePart is { } notesPart)
                {
                    foreach (var textElem in notesPart.NotesSlide.Descendants<A.Text>())
                    {
                        if (maxReplacements > 0 && count >= maxReplacements) break;
                        if (textElem.Text.Contains(oldText, StringComparison.Ordinal))
                        {
                            textElem.Text = textElem.Text.Replace(oldText, newText);
                            count++;
                        }
                    }
                }

                slidePart.Slide.Save();
            }

            return JsonSerializer.Serialize(new { replacements = count }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ReplaceInPresentation failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    // ── Inline formatting (Word) ───────────────────────────────────────────

    /// <summary>Parse Markdown-like inline markers into a sequence of Word Run elements.</summary>
    private static IEnumerable<W.Run> ParseInlineRuns(string text)
    {
        var runs = new List<W.Run>();
        int pos = 0;

        bool bold = false, italic = false, underline = false, strike = false;
        string? color = null;
        int fontSize = 0;

        while (pos < text.Length)
        {
            if (Match(text, pos, "{color:"))
            {
                int close = text.IndexOf('}', pos + 7);
                if (close > pos) { color = text.Substring(pos + 7, close - pos - 7); pos = close + 1; continue; }
            }
            if (Match(text, pos, "{/color}")) { color = null; pos += 8; continue; }
            if (Match(text, pos, "{size:"))
            {
                int close = text.IndexOf('}', pos + 6);
                if (close > pos && int.TryParse(text.Substring(pos + 6, close - pos - 6), out var sz))
                { fontSize = sz; pos = close + 1; continue; }
            }
            if (Match(text, pos, "{/size}")) { fontSize = 0; pos += 7; continue; }
            if (Match(text, pos, "**"))  { bold = !bold;      pos += 2; continue; }
            if (Match(text, pos, "__"))  { underline = !underline; pos += 2; continue; }
            if (Match(text, pos, "~~"))  { strike = !strike;   pos += 2; continue; }
            if (text[pos] == '*' && (pos + 1 >= text.Length || text[pos + 1] != '*'))
                { italic = !italic; pos++; continue; }

            int next = FindNextMarker(text, pos);
            var literal = text[pos..next];
            pos = next;

            if (string.IsNullOrEmpty(literal)) continue;

            var run = new W.Run(new W.Text(literal)
                { Space = SpaceProcessingModeValues.Preserve });
            var rpr = new W.RunProperties();
            if (bold)      rpr.AppendChild(new W.Bold());
            if (italic)    rpr.AppendChild(new W.Italic());
            if (underline) rpr.AppendChild(new W.Underline { Val = W.UnderlineValues.Single });
            if (strike)    rpr.AppendChild(new W.Strike());
            if (color is not null) rpr.AppendChild(new W.Color { Val = color });
            if (fontSize > 0)
            {
                rpr.AppendChild(new W.FontSize { Val = (fontSize * 2).ToString() });
                rpr.AppendChild(new W.FontSizeComplexScript { Val = (fontSize * 2).ToString() });
            }
            if (rpr.HasChildren) run.RunProperties = rpr;
            runs.Add(run);
        }

        return runs;
    }

    private static bool Match(string text, int pos, string marker) =>
        pos + marker.Length <= text.Length &&
        text.AsSpan(pos, marker.Length).SequenceEqual(marker.AsSpan());

    private static int FindNextMarker(string text, int startPos)
    {
        string[] markers = ["**", "__", "~~", "{color:", "{/color}", "{size:", "{/size}"];
        int min = text.Length;
        foreach (var m in markers)
        {
            int idx = text.IndexOf(m, startPos, StringComparison.Ordinal);
            if (idx >= 0 && idx < min) min = idx;
        }
        for (int i = startPos; i < min; i++)
        {
            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
            { min = i; break; }
        }
        return min;
    }

    // ── Document body builder ──────────────────────────────────────────────

    private static void AppendContentToBody(W.Body body, string content, OpenXmlElement? insertBefore = null)
    {
        var lines = content.Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');

            if (line.TrimStart().StartsWith('|'))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].TrimEnd('\r').TrimStart().StartsWith('|'))
                {
                    tableLines.Add(lines[i].TrimEnd('\r'));
                    i++;
                }
                var tbl = BuildWordTable(tableLines);
                if (insertBefore is null) body.AppendChild(tbl);
                else                      body.InsertBefore(tbl, insertBefore);
                continue;
            }

            var para = BuildParagraph(line);
            if (insertBefore is null) body.AppendChild(para);
            else                      body.InsertBefore(para, insertBefore);
            i++;
        }
    }

    private static W.Paragraph BuildParagraph(string line)
    {
        string? styleId = null;
        string text = line;

        if      (line.StartsWith("### ")) { styleId = "Heading3"; text = line[4..]; }
        else if (line.StartsWith("## "))  { styleId = "Heading2"; text = line[3..]; }
        else if (line.StartsWith("# "))   { styleId = "Heading1"; text = line[2..]; }

        var para = new W.Paragraph();
        if (styleId is not null)
        {
            para.ParagraphProperties = new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = styleId });
            para.AppendChild(new W.Run(new W.Text(text)
                { Space = SpaceProcessingModeValues.Preserve }));
        }
        else
        {
            foreach (var run in ParseInlineRuns(text))
                para.AppendChild(run);
            if (!para.HasChildren)
                para.AppendChild(new W.Run(new W.Text("")));
        }

        return para;
    }

    private static W.Table BuildWordTable(List<string> lines)
    {
        var table = new W.Table();
        table.AppendChild(new W.TableProperties(
            new W.TableBorders(
                new W.TopBorder    { Val = W.BorderValues.Single, Size = 4 },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.LeftBorder   { Val = W.BorderValues.Single, Size = 4 },
                new W.RightBorder  { Val = W.BorderValues.Single, Size = 4 },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.InsideVerticalBorder   { Val = W.BorderValues.Single, Size = 4 })));

        foreach (var line in lines)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^\|[-|: ]+\|$"))
                continue;

            var cells = line.Split('|').Skip(1).ToList();
            if (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[^1]))
                cells.RemoveAt(cells.Count - 1);

            var row = new W.TableRow();
            foreach (var cellText in cells)
            {
                var tc = new W.TableCell();
                var p  = new W.Paragraph();
                foreach (var run in ParseInlineRuns(cellText.Trim()))
                    p.AppendChild(run);
                if (!p.HasChildren) p.AppendChild(new W.Run(new W.Text("")));
                tc.AppendChild(p);
                row.AppendChild(tc);
            }
            table.AppendChild(row);
        }

        return table;
    }

    // ── PowerPoint slide builder ───────────────────────────────────────────

    private static void BuildSlide(SlidePart slidePart, SlideSpec spec)
    {
        slidePart.Slide = new Slide(
            new CommonSlideData(
                new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "" },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new A.TransformGroup()),
                    BuildTitleShape(spec.Title),
                    BuildBodyShape(spec.Bullets))));

        if (!string.IsNullOrWhiteSpace(spec.Notes))
        {
            var notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.NotesSlide = new NotesSlide(
                new CommonSlideData(
                    new ShapeTree(
                        new NonVisualGroupShapeProperties(
                            new NonVisualDrawingProperties { Id = 1, Name = "" },
                            new NonVisualGroupShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new GroupShapeProperties(new A.TransformGroup()),
                        BuildNotesShape(spec.Notes))));
        }
    }

    private static Shape BuildTitleShape(string title) =>
        new(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = 2, Name = "Title" },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(
                    new PlaceholderShape { Type = PlaceholderValues.Title })),
            new ShapeProperties(),
            new TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.Text(title)))));

    private static Shape BuildBodyShape(List<string> bullets)
    {
        var body = new TextBody(new A.BodyProperties(), new A.ListStyle());

        foreach (var bullet in bullets)
        {
            bool isSub = bullet.StartsWith("  - ") || bullet.StartsWith("    •");
            var text   = isSub ? bullet.TrimStart([' ', '-', '•']).TrimStart() : bullet;
            int level  = isSub ? 1 : 0;

            var para = new A.Paragraph();
            if (level > 0)
                para.AppendChild(new A.ParagraphProperties { Level = level });
            para.AppendChild(new A.Run(new A.Text(text)));
            body.AppendChild(para);
        }

        if (bullets.Count == 0) body.AppendChild(new A.Paragraph());

        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = 3, Name = "Content" },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(new PlaceholderShape { Index = 1 })),
            new ShapeProperties(),
            body);
    }

    private static Shape BuildNotesShape(string notes) =>
        new(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = 4, Name = "Notes" },
                new NonVisualShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties(
                    new PlaceholderShape { Type = PlaceholderValues.Body, Index = 1 })),
            new ShapeProperties(),
            new TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.Text(notes)))));

    // ── Spreadsheet helpers ────────────────────────────────────────────────

    private static SS.Cell BuildCell(string cellRef, string value)
    {
        var cell = new SS.Cell { CellReference = cellRef };
        ApplyCellValue(cell, value);
        return cell;
    }

    private static void ApplyCellValue(SS.Cell cell, string value)
    {
        if (value.StartsWith('='))
        {
            cell.CellFormula = new SS.CellFormula(value[1..]);
            cell.CellValue   = new SS.CellValue();
            cell.DataType    = null;
        }
        else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            cell.CellValue = new SS.CellValue(value);
            cell.DataType  = null;
        }
        else
        {
            cell.DataType    = SS.CellValues.InlineString;
            cell.InlineString = new SS.InlineString(new SS.Text(value));
        }
    }

    private static string ColumnLetterFromIndex(int index)
    {
        var result = string.Empty;
        index++;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            result = (char)('A' + rem) + result;
            index  = (index - 1) / 26;
        }
        return result;
    }

    private static string FormatNum(double val) =>
        val == Math.Floor(val)
            ? ((long)val).ToString()
            : val.ToString("G6", CultureInfo.InvariantCulture);

    private static string ResolveCellValue(SS.Cell cell, List<string> sharedStrings)
    {
        var raw   = cell.CellValue?.Text ?? string.Empty;
        var dtype = cell.DataType?.Value;

        if (dtype == SS.CellValues.SharedString && int.TryParse(raw, out var idx) && idx < sharedStrings.Count)
            return sharedStrings[idx];
        if (dtype == SS.CellValues.Boolean)
            return raw == "1" ? "TRUE" : "FALSE";
        if (dtype == SS.CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? raw;
        if (dtype == SS.CellValues.Error)
            return $"#ERR:{raw}";
        return raw;
    }

    // ── Slide spec parser ──────────────────────────────────────────────────

    private record SlideSpec(string Title, List<string> Bullets, string Notes);

    private static List<SlideSpec> ParseSlideSpec(string json)
    {
        var arr = JsonSerializer.Deserialize<JsonArray>(json)
            ?? throw new ArgumentException("jsonSlides must be a JSON array.");
        var result = new List<SlideSpec>();
        foreach (var item in arr)
        {
            var title   = item?["title"]?.ToString()  ?? "";
            var bullets = item?["bullets"]?.AsArray().Select(b => b?.ToString() ?? "").ToList() ?? [];
            var notes   = item?["notes"]?.ToString()  ?? "";
            result.Add(new SlideSpec(title, bullets, notes));
        }
        return result;
    }

    // ── PDF export ─────────────────────────────────────────────────────────────

    public string ConvertToPdf(string filePath, string? outputDirectory = null)
    {
        try
        {
            var soffice = ResolveSOffice();
            if (soffice is null)
                return ErrorJson("IoError",
                    "LibreOffice not found. Install LibreOffice and ensure 'soffice' is on PATH. " +
                    "Download: https://www.libreoffice.org/download/");

            var outDir = outputDirectory ?? Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException("Cannot determine output directory.");
            Directory.CreateDirectory(outDir);

            var psi = new ProcessStartInfo
            {
                FileName  = soffice,
                Arguments = $"--headless --convert-to pdf --outdir \"{outDir}\" \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute  = false,
                CreateNoWindow   = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start LibreOffice process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                return ErrorJson("IoError", "PDF conversion timed out after 60 seconds.");
            }

            if (process.ExitCode != 0)
                return ErrorJson("IoError",
                    $"LibreOffice exited with code {process.ExitCode}: {stderrTask.Result.Trim()}");

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var pdfPath  = Path.Combine(outDir, baseName + ".pdf");

            if (!File.Exists(pdfPath))
                return ErrorJson("IoError",
                    $"Conversion appeared to succeed but PDF not found at: {pdfPath}\n" +
                    $"soffice output: {stdoutTask.Result.Trim()}");

            logger.LogDebug("ConvertToPdf: {Src} → {Pdf}", filePath, pdfPath);
            return JsonSerializer.Serialize(new { pdfPath }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ConvertToPdf failed for {Path}: {Msg}", filePath, ex.Message);
            return ErrorJson("IoError", ex.Message);
        }
    }

    private static string? ResolveSOffice()
    {
        foreach (var name in (string[])["soffice", "libreoffice"])
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName  = name,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute = false,
                    CreateNoWindow  = true
                });
                p?.WaitForExit(3000);
                if (p?.ExitCode == 0) return name;
            }
            catch { /* not on PATH */ }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] candidates =
            [
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"LibreOffice\program\soffice.exe"),
            ];
            return candidates.FirstOrDefault(File.Exists);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return File.Exists("/Applications/LibreOffice.app/Contents/MacOS/soffice")
                ? "/Applications/LibreOffice.app/Contents/MacOS/soffice"
                : null;

        // Linux
        string[] linuxCandidates =
        [
            "/usr/bin/soffice",
            "/usr/lib/libreoffice/program/soffice",
            "/opt/libreoffice/program/soffice",
        ];
        return linuxCandidates.FirstOrDefault(File.Exists);
    }

    private static string ErrorJson(string code, string message) =>
        JsonSerializer.Serialize(new { error = code, message }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
}
