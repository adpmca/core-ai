#!/usr/bin/env bash
# Restarts Diva API (port 5062) and admin portal (port 5173).

PORTAL_PORT=5173
API_PORT=5062

kill_port() {
  local port=$1
  echo "Stopping processes on port $port..."
  powershell.exe -NoProfile -Command "
    \$pids = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
             Select-Object -ExpandProperty OwningProcess -Unique
    if (\$pids) {
      \$pids | ForEach-Object { Stop-Process -Id \$_ -Force -ErrorAction SilentlyContinue }
      Write-Host \"Killed PID(s): \$(\$pids -join ', ')\"
    } else {
      Write-Host 'No process found.'
    }
  "
}

kill_port $PORTAL_PORT
kill_port $API_PORT

echo ""
echo "Applying database migrations..."
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
  --project src/Diva.Infrastructure \
  --startup-project src/Diva.Host \
  -- --provider SQLite

echo ""
echo "Starting Diva API on port $API_PORT (new window)..."
powershell.exe -NoProfile -Command "Start-Process powershell -ArgumentList '-NoExit', '-Command', 'Set-Location C:\Apps\dev\diva-ai; dotnet run --project src\Diva.Host --launch-profile http'"

echo "Starting admin portal on port $PORTAL_PORT..."
cmd.exe /c "cd /d C:\Apps\dev\diva-ai\admin-portal && npm run dev"
