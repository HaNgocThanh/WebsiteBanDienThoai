param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
$previousSql = $env:PHONESTORE_TEST_SQL
try {
    # Read only the required key; never print env file or credentials.
    $line = Get-Content -LiteralPath (Join-Path $repoRoot '.env') | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } | Select-Object -First 1
    if (!$line) { throw 'Missing MSSQL_SA_PASSWORD in local .env.' }
    $testPassword = $line.Substring('MSSQL_SA_PASSWORD='.Length).Trim().Trim('"').Trim("'")
    $escapedPassword = $testPassword.Replace('"', '""')
    $env:PHONESTORE_TEST_SQL = 'Server=127.0.0.1,14330;Database=PhoneStore_Test_Local;User Id=sa;Password="' + $escapedPassword + '";Encrypt=True;TrustServerCertificate=True;'
    dotnet test tests/PhoneStore.UnitTests/PhoneStore.UnitTests.csproj --nologo --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
    dotnet test tests/PhoneStore.IntegrationTests/PhoneStore.IntegrationTests.csproj --nologo --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
} finally {
    $env:PHONESTORE_TEST_SQL = $previousSql
    Pop-Location
}


