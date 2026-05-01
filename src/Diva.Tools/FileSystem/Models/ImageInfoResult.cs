namespace Diva.Tools.FileSystem.Models;

public sealed record ImageInfoResult(
    string Format,
    int Width,
    int Height,
    long FileSizeBytes,
    double BlurScore,
    string FocusQuality,        // "sharp" | "blurry"
    double MeanBrightness,
    string ExposureQuality,     // "normal" | "underexposed" | "overexposed"
    string OverallQuality,      // "good" | "degraded" | "poor"
    Dictionary<string, string> Exif,
    string? ThumbnailBase64,
    string? ImageBase64);
