using Diva.Tools.FileSystem.Models;

namespace Diva.Tools.FileSystem.Abstractions;

public interface IImageReader
{
    /// <summary>
    /// Analyzes image: metadata, EXIF, quality metrics, optional base64.
    /// Returns <see cref="ImageInfoResult"/>. Does NOT throw on valid image files.
    /// </summary>
    ImageInfoResult Analyze(string filePath, ImageOptions opts);
}
