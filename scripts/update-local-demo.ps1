#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$efArguments = @(
    'ef', 'database', 'update',
    '--project', (Join-Path $repoRoot 'src/Tindy.Infrastructure/Tindy.Infrastructure.csproj'),
    '--startup-project', (Join-Path $repoRoot 'src/Tindy.Api/Tindy.Api.csproj'),
    '--context', 'TindyDbContext',
    '--', '--environment', 'Development'
)

$previousEnabled = [Environment]::GetEnvironmentVariable('DevelopmentSeed__Enabled', 'Process')
$previousRequireLoopback = [Environment]::GetEnvironmentVariable('DevelopmentSeed__RequireLoopback', 'Process')
try {
    $env:DevelopmentSeed__Enabled = 'true'
    $env:DevelopmentSeed__RequireLoopback = 'true'
    dotnet @efArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Local database update and demo seed failed (dotnet ef exit code $LASTEXITCODE)."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('DevelopmentSeed__Enabled', $previousEnabled, 'Process')
    [Environment]::SetEnvironmentVariable('DevelopmentSeed__RequireLoopback', $previousRequireLoopback, 'Process')
}
