param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$runId = [Guid]::NewGuid().ToString('N')
$database = 'PhoneStore_Test_' + $runId
$mailbox = Join-Path ([IO.Path]::GetTempPath()) ('PhoneStore_Test_Mail_' + $runId)
$previousConnection = $env:ConnectionStrings__DefaultConnection
$previousSql = $env:PHONESTORE_E2E_SQL
$previousMailbox = $env:PHONESTORE_E2E_MAILBOX
$previousImages = $env:PHONESTORE_E2E_IMAGES
$images = Join-Path ([IO.Path]::GetTempPath()) ('PhoneStore_Test_Images_' + $runId)
$previousLogLevel = $env:Logging__LogLevel__Default
$created = $false
function Invoke-TestSql([string]$query) {
    $query | docker compose exec -T db sh -c 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -i /dev/stdin'
    if ($LASTEXITCODE -ne 0) { throw 'Test SQL command failed.' }
}
Push-Location $repoRoot
try {
    if ($database -notmatch '^PhoneStore_Test_[0-9a-f]{32}$') { throw 'Unsafe test DB name.' }
    $line = Get-Content -LiteralPath (Join-Path $repoRoot '.env') | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } | Select-Object -First 1
    if (!$line) { throw 'Missing local SQL password configuration.' }
    $testPassword = $line.Substring('MSSQL_SA_PASSWORD='.Length).Trim().Trim('"').Trim("'").Replace('"','""')
    $env:ConnectionStrings__DefaultConnection = 'Server=127.0.0.1,14330;Database=' + $database + ';User Id=sa;Password="' + $testPassword + '";Encrypt=True;TrustServerCertificate=True;'
    $env:Logging__LogLevel__Default = 'Warning'
    Invoke-TestSql "CREATE DATABASE [$database];"
    $created = $true
    Push-Location backend
    try {
        dotnet ef database update --project PhoneStore.Api --no-color
        if ($LASTEXITCODE -ne 0) { throw 'Test migration failed.' }
    } finally { Pop-Location }
    New-Item -ItemType Directory -Path $mailbox | Out-Null
    $env:PHONESTORE_E2E_SQL = $env:ConnectionStrings__DefaultConnection
    $env:PHONESTORE_E2E_MAILBOX = $mailbox
    New-Item -ItemType Directory -Path $images | Out-Null
    $env:PHONESTORE_E2E_IMAGES = $images
    npm --prefix frontend run test:e2e -- --config playwright.catalog.config.ts
    if ($LASTEXITCODE -ne 0) { throw 'Catalog browser tests failed.' }
} finally {
    $env:ConnectionStrings__DefaultConnection = $previousConnection
    $env:PHONESTORE_E2E_SQL = $previousSql
    $env:PHONESTORE_E2E_MAILBOX = $previousMailbox
    $env:PHONESTORE_E2E_IMAGES = $previousImages
    $env:Logging__LogLevel__Default = $previousLogLevel
    if ($created) { Invoke-TestSql "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];" }
    $resolvedMailbox = [IO.Path]::GetFullPath($mailbox)
    $expectedMailbox = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('PhoneStore_Test_Mail_' + $runId)))
    if ($resolvedMailbox -ne $expectedMailbox) { throw 'Unsafe mailbox cleanup path.' }
    if (Test-Path -LiteralPath $resolvedMailbox) { Remove-Item -LiteralPath $resolvedMailbox -Recurse -Force }
    $resolvedImages = [IO.Path]::GetFullPath($images)
    $expectedImages = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('PhoneStore_Test_Images_' + $runId)))
    if ($resolvedImages -ne $expectedImages) { throw 'Unsafe image cleanup path.' }
    if (Test-Path -LiteralPath $resolvedImages) { Remove-Item -LiteralPath $resolvedImages -Recurse -Force }
    Pop-Location
}
