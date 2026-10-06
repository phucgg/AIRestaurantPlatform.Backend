# Identity password hash utility

Run from the repository root:

```powershell
dotnet run --project tools/Identity.PasswordHash -c Release
```

Enter the password at the hidden interactive prompt. Do not put a password in command arguments, shell history, CI, or logs. The utility prints only the resulting hash to standard output for manual use in the approved database script. Treat that output as sensitive and do not commit it.

The utility uses ASP.NET Core `PasswordHasher`, IdentityV3, with 100,000 iterations. The API must use the same configuration when implemented. No database connection is made and no user is created.

Database insertion instructions are pending confirmation of the existing schema and exact administrator role name.
