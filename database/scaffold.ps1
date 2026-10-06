param([switch]$Force)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repositoryRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'EF tool restore failed.' }
    $scaffoldArguments = @('ef', 'dbcontext', 'scaffold', 'Name=ConnectionStrings:Identity',
        'Microsoft.EntityFrameworkCore.SqlServer', '--project', 'IdentityService.API',
        '--context', 'IdentityDbContext', '--context-dir', 'Data', '--output-dir', 'Models',
        '--table', 'dbo.Roles', '--table', 'dbo.Users', '--table', 'dbo.UserSessions',
        '--table', 'dbo.AuditLogs', '--no-onconfiguring')
    if ($Force) { $scaffoldArguments += '--force' }
    dotnet @scaffoldArguments
    if ($LASTEXITCODE -ne 0) { throw 'Identity reverse engineering failed.' }
} finally {
    Pop-Location
}
