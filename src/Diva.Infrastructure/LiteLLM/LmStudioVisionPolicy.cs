using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Pipeline policy that fixes base64 encoding in data-URI image URLs for LM Studio / llama.cpp.
///
/// LM Studio expects the standard OpenAI format: "url": "data:image/jpeg;base64,BASE64DATA"
/// However, .NET's Uri class percent-encodes base64 characters (+→%2B, =→%3D, /→%2F) when
/// DataContent is constructed from a BinaryData/byte[] overload, making the base64 undecodable.
///
/// This policy finds data-URI base64 payloads and percent-decodes them back to plain base64.
/// It does NOT strip the "data:TYPE;base64," prefix — LM Studio requires it.
/// </summary>
internal sealed class LmStudioVisionPolicy : PipelinePolicy
{
    internal static readonly LmStudioVisionPolicy Instance = new();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        FixPercentEncodedBase64(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        FixPercentEncodedBase64(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
    }

    private static void FixPercentEncodedBase64(PipelineMessage message)
    {
        if (message.Request.Content is null) return;

        using var ms = new MemoryStream();
        message.Request.Content.WriteTo(ms, CancellationToken.None);
        var body = Encoding.UTF8.GetString(ms.ToArray());

        if (!body.Contains(";base64,", StringComparison.Ordinal)) return;
        if (!body.Contains('%')) return; // no percent-encoding — nothing to fix

        var result = DecodeBase64Payloads(body);
        if (ReferenceEquals(result, body)) return;

        message.Request.Content = BinaryContent.Create(BinaryData.FromString(result));
    }

    /// <summary>
    /// Finds <c>;base64,PAYLOAD"</c> segments and applies <see cref="Uri.UnescapeDataString"/>
    /// to the PAYLOAD when .NET's <see cref="Uri"/> class has percent-encoded base64 characters.
    /// The <c>data:TYPE;base64,</c> prefix is preserved — LM Studio requires it.
    /// </summary>
    private static string DecodeBase64Payloads(string json)
    {
        const string base64Tag = ";base64,";
        if (!json.Contains(base64Tag, StringComparison.Ordinal)) return json;
        if (!json.Contains('%')) return json;

        var sb  = new StringBuilder(json.Length);
        var pos = 0;

        while (pos < json.Length)
        {
            var tagIdx = json.IndexOf(base64Tag, pos, StringComparison.Ordinal);
            if (tagIdx < 0)
            {
                sb.Append(json, pos, json.Length - pos);
                break;
            }

            var afterTag = tagIdx + base64Tag.Length;
            sb.Append(json, pos, afterTag - pos); // keep everything up to and including ";base64,"

            var endIdx = json.IndexOf('"', afterTag);
            if (endIdx < 0)
            {
                sb.Append(json, afterTag, json.Length - afterTag);
                break;
            }

            var payload = json[afterTag..endIdx];
            sb.Append(payload.Contains('%') ? Uri.UnescapeDataString(payload) : payload);
            pos = endIdx;
        }

        return sb.ToString();
    }
}
