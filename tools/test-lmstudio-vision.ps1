param(
    [string]$Endpoint  = "http://100.125.25.31:1234",
    [string]$Model     = "google/gemma-4-e4b",
    [string]$ImagePath = ""
)

# 1x1 green PNG - minimal valid PNG
$PNG_B64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVQI12Nk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="

# 1x1 white JPEG - minimal valid JPEG
$JPEG_B64 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAADs/8QAFAEBAAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AJYA/9k="

$RealB64  = $null
$RealMime = $null
if ($ImagePath -and (Test-Path $ImagePath)) {
    $bytes    = [System.IO.File]::ReadAllBytes($ImagePath)
    $RealB64  = [Convert]::ToBase64String($bytes)
    $ext      = [System.IO.Path]::GetExtension($ImagePath).ToLower()
    $RealMime = if ($ext -eq ".png") { "image/png" } else { "image/jpeg" }
    Write-Host "Loaded: $ImagePath ($([math]::Round($bytes.Length/1024,1)) KB)"
}

function Send-Request([string]$Label, [object]$BodyObj) {
    Write-Host ""
    Write-Host "=== $Label ==="
    $json = $BodyObj | ConvertTo-Json -Depth 10 -Compress
    try {
        $r = Invoke-RestMethod -Uri "$Endpoint/v1/chat/completions" `
                               -Method POST `
                               -ContentType "application/json; charset=utf-8" `
                               -Body $json `
                               -TimeoutSec 60 `
                               -ErrorAction Stop
        Write-Host "PASS: $($r.choices[0].message.content)"
    } catch {
        $status = ""
        $body   = ""
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $body   = $_.ErrorDetails.Message
            if (-not $body) {
                try {
                    $s = $_.Exception.Response.GetResponseStream()
                    $body = [System.IO.StreamReader]::new($s).ReadToEnd()
                } catch {}
            }
        }
        Write-Host "FAIL HTTP $status : $body"
    }
}

Write-Host "Endpoint: $Endpoint   Model: $Model"

# Sanity: text only
Send-Request "TEXT ONLY (no image)" @{
    model      = $Model
    max_tokens = 50
    messages   = @(@{ role = "user"; content = "Reply with just the word hello." })
}

# PNG with data URI
Send-Request "PNG  + data:image/png;base64,PREFIX" @{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = "data:image/png;base64,$PNG_B64" } }
            @{ type = "text";      text       = "What do you see?" }
        )
    })
}

# PNG raw base64
Send-Request "PNG  + raw base64 (no prefix)" @{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = $PNG_B64 } }
            @{ type = "text";      text       = "What do you see?" }
        )
    })
}

# JPEG with data URI
Send-Request "JPEG + data:image/jpeg;base64,PREFIX" @{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = "data:image/jpeg;base64,$JPEG_B64" } }
            @{ type = "text";      text       = "What do you see?" }
        )
    })
}

# JPEG raw base64
Send-Request "JPEG + raw base64 (no prefix)" @{
    model      = $Model
    max_tokens = 100
    messages   = @(@{
        role    = "user"
        content = @(
            @{ type = "image_url"; image_url = @{ url = $JPEG_B64 } }
            @{ type = "text";      text       = "What do you see?" }
        )
    })
}

# Real image if provided
if ($RealB64) {
    Send-Request "REAL + data:$RealMime;base64,PREFIX" @{
        model      = $Model
        max_tokens = 200
        messages   = @(@{
            role    = "user"
            content = @(
                @{ type = "image_url"; image_url = @{ url = "data:${RealMime};base64,$RealB64" } }
                @{ type = "text";      text       = "What do you see?" }
            )
        })
    }

    Send-Request "REAL + raw base64 (no prefix)" @{
        model      = $Model
        max_tokens = 200
        messages   = @(@{
            role    = "user"
            content = @(
                @{ type = "image_url"; image_url = @{ url = $RealB64 } }
                @{ type = "text";      text       = "What do you see?" }
            )
        })
    }
}

Write-Host ""
Write-Host "Done."
