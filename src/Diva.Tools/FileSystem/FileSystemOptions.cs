namespace Diva.Tools.FileSystem;

public sealed class FileSystemOptions
{
    public const string SectionName = "FileSystem";

    // ── Security ───────────────────────────────────────────────────────────
    public List<string> AllowedBasePaths { get; set; } = [];
    public List<string> DenyFilePatterns { get; set; } =
    [
        "*.key", "*.pfx", "*.p12", "*.pem", "*.cer",
        ".env", ".env.*", "*.secret", "appsettings*.json",
        "id_rsa", "id_ed25519", "*.kubeconfig"
    ];
    public bool FollowSymlinks { get; set; } = false;
    public bool AllowWrites { get; set; } = false;
    // When true, the run_script tool is available. Off by default — must be explicitly opted in.
    // run_script executes bash scripts (bash on Linux/macOS, Git Bash / WSL bash on Windows).
    public bool AllowScript { get; set; } = false;
    public int ScriptTimeoutSeconds { get; set; } = 30;
    // Max bash scripts that may run simultaneously across all clients (0 = unlimited).
    public int MaxConcurrentScripts { get; set; } = 5;
    // Max MCP requests per minute per client IP (0 = disabled).
    public int RateLimitPerMinute { get; set; } = 120;

    // ── Tool availability ──────────────────────────────────────────────────
    // Empty = all tools enabled. Non-empty = only listed names enabled.
    // Valid names: read_file, read_pdf, get_image_info, read_image, list_directory,
    //              get_file_info, search_files, get_allowed_roots,
    //              list_zip, read_zip_entry,
    //              write_file, append_file, copy_file, create_directory,
    //              delete_file, delete_directory, move_item, run_script,
    //              read_document, read_spreadsheet, read_presentation, search_in_document,
    //              write_document, append_to_document, replace_in_document,
    //              write_spreadsheet, update_cells, create_pivot_summary,
    //              write_presentation, append_slides, replace_in_presentation,
    //              convert_to_pdf
    public List<string> EnabledTools { get; set; } = [];

    // ── Limits ─────────────────────────────────────────────────────────────
    public long MaxReadFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxDirectoryListEntries { get; set; } = 500;
    public int MaxSearchResults { get; set; } = 200;

    // ── File type flags ────────────────────────────────────────────────────
    public bool TextEnabled { get; set; } = true;
    public bool PdfEnabled { get; set; } = true;
    public bool ImagesEnabled { get; set; } = true;
    public bool OfficeEnabled { get; set; } = true;

    // ── Sub-options ────────────────────────────────────────────────────────
    public PdfOptions Pdf { get; set; } = new();
    public ImageOptions Image { get; set; } = new();
    public OfficeOptions Office { get; set; } = new();

    // ── Standalone HTTP mode only ──────────────────────────────────────────
    // Non-empty: requests must send "Authorization: Bearer <key>" or "X-Api-Key: <key>".
    // Empty = no auth (trust network / OS-level security).
    public string? StandaloneApiKey { get; set; }
}

public sealed class PdfOptions
{
    public int MaxPages { get; set; } = 100;
    public bool IncludeMetadata { get; set; } = true;
    public bool IncludePageNumbers { get; set; } = true;
}

public sealed class ImageOptions
{
    public bool ExtractExif { get; set; } = true;
    public bool ComputeQualityMetrics { get; set; } = true;
    public bool ReturnBase64 { get; set; } = false;
    // When ReturnBase64=true, resize the image so its longest side does not exceed this value before
    // encoding. 0 = no resize. Default 1568 matches Claude's vision input limit.
    public int Base64MaxDimensionPx { get; set; } = 1200;
    public bool ReturnThumbnail { get; set; } = true;
    public int ThumbnailMaxDimension { get; set; } = 256;
    public long MaxImageFileSizeBytes { get; set; } = 20 * 1024 * 1024;
    public double BlurThreshold { get; set; } = 100.0;
    public double ExposureUnderThreshold { get; set; } = 50.0;
    public double ExposureOverThreshold { get; set; } = 205.0;

    // ── Phase 24 placeholders (not wired yet) ──────────────────────────────
    public bool ClassificationEnabled { get; set; } = false;
    public string? ClassificationApiKey { get; set; }
    public string ClassificationModel { get; set; } = "claude-haiku-4-5-20251001";
    public List<string> Categories { get; set; } = [];
    public string ClassificationPrompt { get; set; } = "";
}

public sealed class OfficeOptions
{
    public bool IncludeWordTables   { get; set; } = true;
    public bool IncludeWordComments { get; set; } = false;
    public int  MaxSheetsToRead     { get; set; } = 10;
    public int  MaxRowsPerSheet     { get; set; } = 1000;
    public int  MaxSlidesToRead     { get; set; } = 50;
    public bool IncludeSpeakerNotes { get; set; } = true;
    public bool IncludeMetadata     { get; set; } = true;
}
