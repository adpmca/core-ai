using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Diva.Host.Controllers;

/// <summary>
/// Temporary diagnostic endpoint — tests which image format a local LLM endpoint accepts.
/// DELETE this controller once vision format is confirmed.
/// </summary>
[ApiController]
[Route("api/debug/vision-probe")]
[AllowAnonymous]
public class VisionProbeController : ControllerBase
{
    // Minimal 1x1 green PNG (valid)
    private const string PngB64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVQI12Nk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    // Minimal 1x1 white JPEG (valid — /9j/ = FF D8 FF magic bytes)
    private const string JpegB64 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAADs/8QAFAEBAAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AJYA/9k=";

    [HttpGet]
    public async Task<IActionResult> Probe(
        [FromQuery] string endpoint = "http://host.docker.internal:4141",
        [FromQuery] string model    = "google/gemma-4-e4b")
    {
        var results = new List<object>();

        results.Add(await TestAsync("TEXT ONLY (sanity)",
            endpoint, model, BuildTextBody(model)));

        results.Add(await TestAsync("PNG  | data:image/png;base64,...",
            endpoint, model, BuildImageBody(model, $"data:image/png;base64,{PngB64}")));

        results.Add(await TestAsync("PNG  | raw base64 (no prefix)",
            endpoint, model, BuildImageBody(model, PngB64)));

        results.Add(await TestAsync("JPEG | data:image/jpeg;base64,...",
            endpoint, model, BuildImageBody(model, $"data:image/jpeg;base64,{JpegB64}")));

        results.Add(await TestAsync("JPEG | raw base64 (no prefix)",
            endpoint, model, BuildImageBody(model, JpegB64)));

        return Ok(new { endpoint, model, results });
    }

    private static async Task<object> TestAsync(string name, string endpoint, string model, string body)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/v1/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp    = await http.SendAsync(req);
            var rawBody = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(rawBody);
                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                return new { name, pass = true, response = text };
            }

            return new { name, pass = false, status = (int)resp.StatusCode, error = rawBody };
        }
        catch (Exception ex)
        {
            return new { name, pass = false, error = ex.Message };
        }
    }

    private static string BuildTextBody(string model) =>
        JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 30,
            messages   = new[] { new { role = "user", content = "Say hello." } }
        });

    /// <summary>
    /// Tests SummarizeImageAsync logic end-to-end with a real image supplied as base64.
    /// POST body: { "imageBase64": "...", "mediaType": "image/jpeg", "endpoint": "...", "model": "..." }
    /// Replicates the exact HttpClient call that OpenAiProviderStrategy.SummarizeImageAsync makes.
    /// </summary>
    [HttpPost("summarize")]
    public async Task<IActionResult> Summarize([FromBody] SummarizeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ImageBase64))
            return BadRequest("imageBase64 is required");

        var endpoint = (req.Endpoint ?? "http://host.docker.internal:4141").TrimEnd('/');
        if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            endpoint += "/v1";

        var model    = req.Model ?? "google/gemma-4-e4b";
        var mime     = req.MediaType ?? "image/jpeg";
        var imageUrl = $"data:{mime};base64,{req.ImageBase64}";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };

            var pass1 = await VisionPassAsync(http, endpoint, model, imageUrl,
                "You are an inventory scanner. List EVERY physical object, product, item, and person visible in this image. " +
                "For each: name/type, brand (if readable), color, quantity, and position (e.g. top-left shelf, center foreground). " +
                "Include even partially visible items. Be exhaustive — missing an item is a critical error.");

            var pass2 = await VisionPassAsync(http, endpoint, model, imageUrl,
                "Read and transcribe ALL text visible in this image. " +
                "Include product names, brand names, prices, barcodes, shelf labels, signs, stickers, handwriting. " +
                "Group by location (e.g. top shelf, front display). Transcribe exactly as written.");

            return Ok(new
            {
                pass = true,
                objects = pass1.Text,
                text    = pass2.Text,
                combined = $"[Objects & Inventory]\n{pass1.Text}\n\n[Text & Labels]\n{pass2.Text}",
                pass1Error = pass1.Error,
                pass2Error = pass2.Error
            });
        }
        catch (Exception ex)
        {
            return Ok(new { pass = false, error = ex.Message });
        }
    }

    private static async Task<(string Text, string? Error)> VisionPassAsync(
        HttpClient http, string endpoint, string model, string imageUrl, string prompt)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 2048,
            messages   = new[]
            {
                new
                {
                    role    = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = imageUrl } },
                        new { type = "text", text = prompt }
                    }
                }
            }
        });

        try
        {
            var resp    = await http.PostAsync($"{endpoint}/chat/completions",
                new StringContent(body, Encoding.UTF8, "application/json"));
            var rawBody = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(rawBody);
                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";
                return (text, null);
            }
            return ("", $"HTTP {(int)resp.StatusCode}: {rawBody}");
        }
        catch (Exception ex)
        {
            return ("", ex.Message);
        }
    }

    private static string BuildImageBody(string model, string urlValue) =>
        JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 80,
            messages   = new[]
            {
                new
                {
                    role    = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = urlValue } },
                        new { type = "text", text = "What color is this image? One word." }
                    }
                }
            }
        });
}

public record SummarizeRequest(
    string? ImageBase64,
    string? MediaType,
    string? Endpoint,
    string? Model);
