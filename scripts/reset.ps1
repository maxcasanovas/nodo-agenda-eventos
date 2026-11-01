# Wipes volumes (clean DB with seeds) and rebuilds everything.
# docker writes its progress to stderr; Windows PowerShell 5.1 would turn that into errors under 'Stop',
# so success is decided by the exit code of each command instead.
Set-Location (Join-Path $PSScriptRoot '..')

docker compose down -v
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

docker compose up --build -d
exit $LASTEXITCODE
