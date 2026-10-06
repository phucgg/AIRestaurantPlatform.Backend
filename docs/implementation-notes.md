# Identity implementation and verification

Project: **AI-Powered Smart Restaurant Platform**. Schema management: **Database First**.

## Changed existing files

- `AIRestaurantPlatform.Backend.slnx`: includes the actual test project and hash utility.
- `IdentityService.API/IdentityService.API.csproj`: EF Core SQL Server/Design and JwtBearer 9.0.14, still net9.0.
- `IdentityService.API/Program.cs`: validated configuration, DI, SQL Server, password hashing, JWT/session authentication, authorization and generic error responses.
- `IdentityService.API/appsettings.json`: non-secret JWT issuer/audience/lifetime defaults.
- `IdentityService.API/IdentityService.API.http`: four supported endpoint examples with private client variables.
- `.dockerignore`: excludes local test artifacts and Python cache from build context.
- Removed template-only `IdentityService.API/WeatherForecast.cs` and `Controllers/WeatherForecastController.cs`.

## Added files

- `database/identity.sql`: non-destructive DDL for exactly the four approved tables.
- `database/scaffold.ps1`, `.config/dotnet-tools.json`: repeatable, table-restricted EF Core 9 reverse engineering, without OnConfiguring.
- `IdentityService.API/Models/{Role,User,UserSession,AuditLog}.cs`, `Data/IdentityDbContext.cs`: generated from the real SQL Server test schema.
- `IdentityService.API/Contracts/AuthContracts.cs`: request validation and explicit safe responses.
- `IdentityService.API/Security/{IdentityRoles,JwtSettings,IdentityDatabaseSettings,SessionTokenValidator}.cs`: exact role allowlist, settings and live server-side session validation.
- `IdentityService.API/Services/IdentityAuthService.cs`: transactions for login, logout and Admin creation; Waiter row locking and sanitized audit.
- `IdentityService.API/Controllers/{AuthController,AdminUsersController}.cs`: only the four requested endpoints.
- `IdentityService.Tests/{IdentityService.Tests.csproj,PasswordHasherTests.cs,SqlIdentityFixture.cs,IdentityBusinessTests.cs}`: meaningful hashing and SQL Server/API integration tests.
- `.github/workflows/identity-ci.yml`, `ci/{required-tests.txt,test-report.py,test_report_tests.py}`: .NET 9 CI, isolated SQL Server initialized by DDL, fail-safe required test checks, error Summary and log/TRX artifacts.
- `tools/Identity.PasswordHash/{Identity.PasswordHash.csproj,Program.cs,README.md}`: standalone interactive hash-only utility with no database dependency.
- `global.json`, `.gitignore`, `README.md`, `docs/identity-schema-proposal.md`, this document: SDK selection, exclusions, complete configuration/SSMS/manual Admin/scaffold/test/Actions documentation.

## Actual local verification (2026-10-07, Asia/Saigon)

- SDK **9.0.318**; application, utility and tests target **net9.0**.
- `dotnet restore AIRestaurantPlatform.Backend.slnx`: successful.
- `dotnet build AIRestaurantPlatform.Backend.slnx -c Release --no-restore`: successful, **0 warnings / 0 errors**.
- SQL Server **2022**, dedicated temporary Linux Docker container, separate port and generated test credentials. No application database or pre-existing service container was touched.
- The same `database/identity.sql` was executed on SQL Server before real EF scaffolding. The scaffold helper was also rerun successfully.
- `dotnet test IdentityService.Tests/IdentityService.Tests.csproj --no-build -c Release`: **61 total / 61 passed / 0 failed / 0 skipped**.
- Waiter concurrency test uses two independent API hosts and repeats simultaneous requests for three rounds, asserting exactly one valid token/session after each round.
- `python -m unittest discover -s ci -p '*_tests.py' -v`: **6 passed**.
- `python ci/test-report.py --verify`: successful; all mandatory methods and minimum theory-case counts were present and passing.
- Workflow YAML parsed successfully and every inline shell script passed `bash -n`; these are static checks, not a GitHub Actions run.
- Logs: local ignored `artifacts/restore.log`, `artifacts/build.log`, `artifacts/test.log`; TRX: `artifacts/TestResults/identity.trx`; report: `artifacts/summary.md`.

## GitHub status and scope limits

Git was initialized with permission, preserving existing source. Remote: `https://github.com/phucgg/AIRestaurantPlatform.Backend.git`. Real workflow runs and their exact commits are available in [Identity CI](https://github.com/phucgg/AIRestaurantPlatform.Backend/actions/workflows/identity-ci.yml). A successful local test result does not establish GitHub Actions PASS; verify each run's conclusion, Summary and artifacts.

No EF migrations were generated/applied, no schema outside the four approved tables was found in the starting code, no application database was changed and no real accounts/roles were seeded. Application schema and the sole Admin are created manually by the owner using the README instructions.
