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

    // ── Tool availability ──────────────────────────────────────────────────
    // Empty = all tools enabled. Non-empty = only listed names enabled.
    // Valid names: read_file, read_pdf, get_image_info, read_image, list_directory,
    //              get_file_info, search_files, get_allowed_roots,
    //              write_file, create_directory, delete_file, move_item
    public List<string> EnabledTools { get; set; } = [];

    // ── Limits ─────────────────────────────────────────────────────────────
    public long MaxReadFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxDirectoryListEntries { get; set; } = 500;
    public int MaxSearchResults { get; set; } = 200;

    // ── File type flags ────────────────────────────────────────────────────
    public bool TextEnabled { get; set; } = true;
    public bool PdfEnabled { get; set; } = true;
    public bool ImagesEnabled { get; set; } = true;

    // ── Sub-options ────────────────────────────────────────────────────────
    public PdfOptions Pdf { get; set; } = new();
    public ImageOptions Image { get; set; } = new();

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
