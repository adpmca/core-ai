param(
    [string]$Endpoint = "http://100.125.25.31:1234",
    [string]$Model    = "google/gemma-4-e4b"
)

# Minimal 1x1 green PNG (valid)
$PNG  = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVQI12Nk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="

# Minimal 1x1 white JPEG (valid - FFD8FF magic bytes -> /9j/ in base64)
$JPEG = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAADs/8QAFAEBAAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AJYA/9k="

function Test-LmStudio([string]$Name, [string]$Body) {
    Write-Host ""
    Write-Host "[$Name]"
    $tmpFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmpFile, $Body, [System.Text.Encoding]::UTF8)
    $result = curl.exe -s -w "`nHTTP_CODE:%{http_code}" `
        -X POST "$Endpoint/v1/chat/completions" `
        -H "Content-Type: application/json" `
        --data-binary "@$tmpFile" `
        --max-time 30 2>&1
    Remove-Item $tmpFile -ErrorAction SilentlyContinue
    Write-Host $result
}

# 1. Text only - sanity check
Test-LmStudio "TEXT ONLY" (@{
    model      = $Model
    max_tokens = 50
    messages   = @(@{ role = "user"; content = "Say hello." })
} | ConvertTo-Json -Depth 5 -Compress)

# 2. PNG data URI
Test-LmStudio "PNG data:image/png;base64,..." (@{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = "data:image/png;base64,$PNG" } }
            @{ type = "text"; text = "What color is this?" }
        )
    })
} | ConvertTo-Json -Depth 10 -Compress)

# 3. PNG raw base64
Test-LmStudio "PNG raw base64 (no prefix)" (@{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = $PNG } }
            @{ type = "text"; text = "What color is this?" }
        )
    })
} | ConvertTo-Json -Depth 10 -Compress)

# 4. JPEG data URI
Test-LmStudio "JPEG data:image/jpeg;base64,..." (@{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = "data:image/jpeg;base64,$JPEG" } }
            @{ type = "text"; text = "What color is this?" }
        )
    })
} | ConvertTo-Json -Depth 10 -Compress)

# 5. JPEG raw base64
Test-LmStudio "JPEG raw base64 (no prefix)" (@{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = $JPEG } }
            @{ type = "text"; text = "What color is this?" }
        )
    })
} | ConvertTo-Json -Depth 10 -Compress)

Write-Host ""
Write-Host "Done."
