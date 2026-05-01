using Diva.Tools.FileSystem.Abstractions;
using Diva.Tools.FileSystem.Models;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Diva.Tools.FileSystem.Readers;

public sealed class ImageReader(ILogger<ImageReader> logger) : IImageReader
{
    public ImageInfoResult Analyze(string filePath, ImageOptions opts)
    {
        using var image = Image.Load<Rgba32>(filePath);
        var fileSize = new FileInfo(filePath).Length;
        var format = image.Metadata.DecodedImageFormat?.Name ?? "Unknown";

        double blurScore = 0;
        double meanBrightness = 0;

        if (opts.ComputeQualityMetrics)
            (blurScore, meanBrightness) = ComputeQualityMetrics(image);

        var focusQuality    = blurScore >= opts.BlurThreshold ? "sharp" : "blurry";
        var exposureQuality = meanBrightness < opts.ExposureUnderThreshold ? "underexposed"
            : meanBrightness > opts.ExposureOverThreshold ? "overexposed"
            : "normal";
        var overallQuality = (focusQuality == "sharp" && exposureQuality == "normal") ? "good"
            : (focusQuality == "blurry" && exposureQuality != "normal") ? "poor"
            : "degraded";

        var exif = opts.ExtractExif ? ExtractExif(image) : new Dictionary<string, string>();

        string? thumbnailBase64 = opts.ReturnThumbnail
            ? BuildThumbnail(image, opts.ThumbnailMaxDimension)
            : null;

        string? imageBase64 = null;
        if (opts.ReturnBase64)
        {
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms);
            imageBase64 = Convert.ToBase64String(ms.ToArray());
        }

        return new ImageInfoResult(
            Format: format,
            Width: image.Width,
            Height: image.Height,
            FileSizeBytes: fileSize,
            BlurScore: Math.Round(blurScore, 2),
            FocusQuality: focusQuality,
            MeanBrightness: Math.Round(meanBrightness, 2),
            ExposureQuality: exposureQuality,
            OverallQuality: overallQuality,
            Exif: exif,
            ThumbnailBase64: thumbnailBase64,
            ImageBase64: imageBase64);
    }

    private static (double blurScore, double meanBrightness) ComputeQualityMetrics(Image<Rgba32> source)
    {
        // ── Convert to grayscale ─────────────────────────────────────────
        using var gray = source.CloneAs<L8>();

        // ── Blur: manual Laplacian variance on grayscale pixels ──────────
        // Kernel [0,-1,0; -1,4,-1; 0,-1,0] applied manually to avoid
        // the IImageProcessor<T> constraint mismatch in ImageSharp 3.x.
        var width  = gray.Width;
        var height = gray.Height;

        // Snapshot all grayscale values to a flat array
        var px = new byte[width * height];
        gray.ProcessPixelRows(acc =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                    px[y * width + x] = row[x].PackedValue;
            }
        });

        // Compute Laplacian variance on interior pixels
        double lapSum = 0, lapSumSq = 0;
        long lapN = 0;
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                int c  = px[y * width + x];
                int t  = px[(y - 1) * width + x];
                int b  = px[(y + 1) * width + x];
                int l  = px[y * width + (x - 1)];
                int r  = px[y * width + (x + 1)];
                double v = Math.Abs(4 * c - t - b - l - r);
                lapSum   += v;
                lapSumSq += v * v;
                lapN++;
            }
        }

        var lapMean  = lapN > 0 ? lapSum / lapN : 0;
        var blurScore = lapN > 1 ? (lapSumSq / lapN) - (lapMean * lapMean) : 0;

        // ── Exposure: mean brightness of grayscale ────────────────────────
        double brightnessSum = 0;
        foreach (var b in px) brightnessSum += b;
        var meanBrightness = px.Length > 0 ? brightnessSum / px.Length : 0;

        return (blurScore, meanBrightness);
    }

    private static string? BuildThumbnail(Image<Rgba32> source, int maxDimension)
    {
        try
        {
            var ratio = Math.Min((double)maxDimension / source.Width,
                                 (double)maxDimension / source.Height);
            var w = Math.Max(1, (int)(source.Width * ratio));
            var h = Math.Max(1, (int)(source.Height * ratio));

            using var thumb = source.Clone(ctx => ctx.Resize(w, h));
            using var ms = new MemoryStream();
            thumb.SaveAsJpeg(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ExtractExif(Image<Rgba32> image)
    {
        var result = new Dictionary<string, string>();
        var profile = image.Metadata.ExifProfile;
        if (profile is null) return result;

        // String tags
        if (profile.TryGetValue(ExifTag.DateTimeOriginal, out var dto) && dto?.Value is not null)
            result["DateTimeOriginal"] = dto.Value;
        if (profile.TryGetValue(ExifTag.Make, out var make) && make?.Value is not null)
            result["Make"] = make.Value;
        if (profile.TryGetValue(ExifTag.Model, out var model) && model?.Value is not null)
            result["Model"] = model.Value;

        // Value-type and Rational tags — compare via non-generic ExifTag (cast is valid)
        foreach (var v in profile.Values)
        {
            var tag = v.Tag;
            if (tag == (ExifTag)ExifTag.Orientation)
                result["Orientation"] = v.ToString() ?? "";
            else if (tag == (ExifTag)ExifTag.XResolution)
                result["XResolution"] = v.ToString() ?? "";
            else if (tag == (ExifTag)ExifTag.YResolution)
                result["YResolution"] = v.ToString() ?? "";
            else if (tag == (ExifTag)ExifTag.GPSLatitude)
                result["GPSLatitude"] = v.ToString() ?? "";
            else if (tag == (ExifTag)ExifTag.GPSLongitude)
                result["GPSLongitude"] = v.ToString() ?? "";
        }

        return result;
    }
}
