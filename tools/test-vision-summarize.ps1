param(
    [Parameter(Mandatory = $true)]
    [string]$ImagePath,

    [string]$AppBase   = "http://localhost:6032",
    [string]$LlmEndpoint = "http://10.0.0.172:1234",
    [string]$Model     = "google/gemma-4-e4b"
)

# Validate file
if (-not (Test-Path $ImagePath)) {
    Write-Error "Image file not found: $ImagePath"
    exit 1
}

$bytes    = [System.IO.File]::ReadAllBytes($ImagePath)
$b64      = [Convert]::ToBase64String($bytes)
$ext      = [System.IO.Path]::GetExtension($ImagePath).ToLower()
$mime     = if ($ext -eq ".png") { "image/png" } else { "image/jpeg" }
$sizekb   = [math]::Round($bytes.Length / 1024, 1)

Write-Host ""
Write-Host "Image : $ImagePath"
Write-Host "Size  : $sizekb KB   MIME: $mime"
Write-Host "App   : $AppBase"
Write-Host "LLM   : $LlmEndpoint   Model: $Model"
Write-Host ""

# ── 1. Quick format probe (direct to LM Studio via the probe endpoint) ─────────
Write-Host "=== FORMAT PROBE (direct GET, 1×1 pixel sanity check) ==="
try {
    $probeUrl = "$AppBase/api/debug/vision-probe?endpoint=$([uri]::EscapeDataString($LlmEndpoint))&model=$([uri]::EscapeDataString($Model))"
    $r = Invoke-RestMethod -Uri $probeUrl -Method GET -TimeoutSec 90 -ErrorAction Stop
    foreach ($item in $r.results) {
        $status = if ($item.pass) { "PASS" } else { "FAIL" }
        Write-Host "  [$status] $($item.name)"
        if ($item.response) { Write-Host "         -> $($item.response)" }
        if ($item.error)    { Write-Host "         -> $($item.error)" }
    }
} catch {
    Write-Host "  Probe error: $_"
}

Write-Host ""

# ── 2. SummarizeImageAsync equivalent — real image via /summarize endpoint ──────
Write-Host "=== SUMMARIZE (real image, replicates SummarizeImageAsync) ==="

$body = @{
    imageBase64 = $b64
    mediaType   = $mime
    endpoint    = $LlmEndpoint
    model       = $Model
} | ConvertTo-Json -Depth 3

try {
    $r = Invoke-RestMethod -Uri "$AppBase/api/debug/vision-probe/summarize" `
                           -Method POST `
                           -ContentType "application/json" `
                           -Body $body `
                           -TimeoutSec 90 `
                           -ErrorAction Stop
    if ($r.pass) {
        Write-Host "  PASS"
        Write-Host ""
        Write-Host $r.response
    } else {
        Write-Host "  FAIL HTTP $($r.status): $($r.error)"
    }
} catch {
    $detail = $_.ErrorDetails.Message
    if (-not $detail -and $_.Exception.Response) {
        try {
            $s = $_.Exception.Response.GetResponseStream()
            $detail = [System.IO.StreamReader]::new($s).ReadToEnd()
        } catch {}
    }
    Write-Host "  ERROR: $_ $detail"
}

Write-Host ""
Write-Host "Done."
