using Diva.Tools.FileSystem;
using Diva.Tools.FileSystem.Readers;
using Diva.Tools.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Diva.Tools.Tests.FileSystem;

public sealed class ImageReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImageReader _reader;

    public ImageReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"diva-img-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _reader = new ImageReader(NullLogger<ImageReader>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreateSharpImage(int width = 100, int height = 100)
    {
        var path = Path.Combine(_tempDir, $"sharp-{Guid.NewGuid():N}.jpg");
        using var img = new Image<Rgba32>(width, height);
        // Draw a checkerboard pattern — high-frequency content = high Laplacian variance
        img.Mutate(ctx => ctx.DrawImage(
            CreateCheckerboard(width, height), new Point(0, 0), 1f));
        img.SaveAsJpeg(path);
        return path;
    }

    private string CreateBlurryImage(int width = 100, int height = 100)
    {
        var path = Path.Combine(_tempDir, $"blurry-{Guid.NewGuid():N}.jpg");
        using var img = CreateCheckerboard(width, height);
        img.Mutate(ctx => ctx.BoxBlur(10)); // heavy blur = low Laplacian variance
        img.SaveAsJpeg(path);
        return path;
    }

    private static Image<Rgba32> CreateCheckerboard(int width, int height)
    {
        var img = new Image<Rgba32>(width, height, Color.White);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if ((x + y) % 2 == 0)
                img[x, y] = Color.Black;
        }
        return img;
    }

    private string CreateDarkImage(int width = 100, int height = 100)
    {
        var path = Path.Combine(_tempDir, $"dark-{Guid.NewGuid():N}.jpg");
        using var img = new Image<Rgba32>(width, height, new Rgba32(10, 10, 10, 255));
        img.SaveAsJpeg(path);
        return path;
    }

    private string CreateBrightImage(int width = 100, int height = 100)
    {
        var path = Path.Combine(_tempDir, $"bright-{Guid.NewGuid():N}.jpg");
        using var img = new Image<Rgba32>(width, height, new Rgba32(250, 250, 250, 255));
        img.SaveAsJpeg(path);
        return path;
    }

    [Fact]
    public void Analyze_ReturnsCorrectDimensions()
    {
        var path = CreateSharpImage(80, 60);
        var result = _reader.Analyze(path, new ImageOptions());

        Assert.Equal(80, result.Width);
        Assert.Equal(60, result.Height);
    }

    [Fact]
    public void Analyze_DetectsJpegFormat()
    {
        var path = CreateSharpImage();
        var result = _reader.Analyze(path, new ImageOptions());

        Assert.Equal("JPEG", result.Format.ToUpperInvariant());
    }

    [Fact]
    public void Analyze_SharpImage_FocusQualityIsSharp()
    {
        var path = CreateSharpImage();
        var opts = new ImageOptions
        {
            ComputeQualityMetrics = true,
            BlurThreshold = 5.0  // lowered — JPEG compression softens even checkerboards
        };

        var result = _reader.Analyze(path, opts);

        Assert.Equal("sharp", result.FocusQuality);
    }

    [Fact]
    public void Analyze_BlurryImage_FocusQualityIsBlurry()
    {
        var path = CreateBlurryImage();
        var opts = new ImageOptions
        {
            ComputeQualityMetrics = true,
            BlurThreshold = 1000.0  // very high threshold → blurry image won't pass
        };

        var result = _reader.Analyze(path, opts);

        Assert.Equal("blurry", result.FocusQuality);
    }

    [Fact]
    public void Analyze_DarkImage_ExposureIsUnderexposed()
    {
        var path = CreateDarkImage();
        var opts = new ImageOptions
        {
            ComputeQualityMetrics = true,
            ExposureUnderThreshold = 50.0
        };

        var result = _reader.Analyze(path, opts);

        Assert.Equal("underexposed", result.ExposureQuality);
    }

    [Fact]
    public void Analyze_BrightImage_ExposureIsOverexposed()
    {
        var path = CreateBrightImage();
        var opts = new ImageOptions
        {
            ComputeQualityMetrics = true,
            ExposureOverThreshold = 205.0
        };

        var result = _reader.Analyze(path, opts);

        Assert.Equal("overexposed", result.ExposureQuality);
    }

    [Fact]
    public void Analyze_ReturnThumbnail_Base64NotNull()
    {
        var path = CreateSharpImage(200, 200);
        var opts = new ImageOptions { ReturnThumbnail = true, ThumbnailMaxDimension = 50 };

        var result = _reader.Analyze(path, opts);

        Assert.NotNull(result.ThumbnailBase64);
        Assert.NotEmpty(result.ThumbnailBase64);
    }

    [Fact]
    public void Analyze_ThumbnailDisabled_Base64IsNull()
    {
        var path = CreateSharpImage();
        var opts = new ImageOptions { ReturnThumbnail = false, ReturnBase64 = false };

        var result = _reader.Analyze(path, opts);

        Assert.Null(result.ThumbnailBase64);
        Assert.Null(result.ImageBase64);
    }

    [Fact]
    public void Analyze_ReturnBase64_FullImageIncluded()
    {
        var path = CreateSharpImage(50, 50);
        var opts = new ImageOptions { ReturnBase64 = true };

        var result = _reader.Analyze(path, opts);

        Assert.NotNull(result.ImageBase64);
        Assert.NotEmpty(result.ImageBase64);
    }

    [Fact]
    public void Analyze_FileSizeIsPositive()
    {
        var path = CreateSharpImage();
        var result = _reader.Analyze(path, new ImageOptions());

        Assert.True(result.FileSizeBytes > 0);
    }
}
