# AI-Powered Smart Restaurant Platform — Identity schema proposal

Status: mapping approved; Database First. The source of truth is `database/identity.sql`. Entity and DbContext were reverse engineered from an isolated SQL Server database with EF Core 9.0.14, restricting scaffolding to the four Identity tables and excluding OnConfiguring. No application database was accessed or modified. There are no EF migrations.

## Columns

All strings use Unicode SQL Server types. All dates represent UTC using `datetime2(7)` and are supplied by the application. No extra columns or tables are proposed.

| Table | Column | SQL Server type | Nullable | Constraint / meaning |
|---|---|---|---|---|
| Role | RoleId | int IDENTITY(1,1) | No | Primary key |
| Role | RoleName | nvarchar(100) | No | Unique, nonempty; exact approved role names |
| Role | Description | nvarchar(500) | Yes | Optional description |
| User | UserId | int IDENTITY(1,1) | No | Primary key |
| User | RoleId | int | No | FK to Role.RoleId |
| User | UserName | nvarchar(100) | No | Unique, nonempty; login identifier |
| User | Email | nvarchar(254) | Yes | Optional contact; no uniqueness requirement |
| User | PhoneNumber | nvarchar(30) | Yes | Optional contact; no uniqueness requirement |
| User | FullName | nvarchar(200) | No | Nonempty |
| User | AvatarUrl | nvarchar(2048) | Yes | Optional HTTP(S) URL |
| User | PasswordHash | nvarchar(512) | No | ASP.NET Core PasswordHasher IdentityV3; never exposed/logged |
| User | IsActive | bit | No | Supplied explicitly by application/manual insert |
| User | CreatedAt | datetime2(7) | No | UTC creation timestamp |
| User | UpdatedAt | datetime2(7) | Yes | Null until changed |
| UserSession | UserSessionId | int IDENTITY(1,1) | No | Primary key |
| UserSession | UserId | int | No | FK to User.UserId |
| UserSession | IpAddress | nvarchar(45) | Yes | IPv4/IPv6; direct connection address |
| UserSession | UserAgent | nvarchar(512) | Yes | Bounded request metadata |
| UserSession | DeviceType | nvarchar(50) | Yes | Optional request metadata; not authorization input |
| UserSession | LoggedInAt | datetime2(7) | No | UTC login time |
| UserSession | ExpiresAt | datetime2(7) | No | UTC expiration; logout sets to current UTC |
| AuditLog | AuditLogId | int IDENTITY(1,1) | No | Primary key |
| AuditLog | ActorUserId | int | No | FK to User.UserId; known actor required |
| AuditLog | Action | nvarchar(100) | No | Bounded event name |
| AuditLog | EntityName | nvarchar(100) | No | Bounded object type name |
| AuditLog | EntityId | nvarchar(128) | Yes | Object identifier text; accepts int today, GUID/composite IDs later without a fixed FK |
| AuditLog | OldValue | nvarchar(max) | Yes | Sanitized JSON; no passwords, hashes, JWTs or secrets |
| AuditLog | NewValue | nvarchar(max) | Yes | Sanitized JSON; no passwords, hashes, JWTs or secrets |
| AuditLog | IpAddress | nvarchar(45) | Yes | Direct connection address |
| AuditLog | CreatedAt | datetime2(7) | No | UTC event time |

## Constraints and indexes

- Physical table names: `Roles`, `Users`, `UserSessions`, `AuditLogs`, all in `dbo`.
- All four primary keys clustered by default. All foreign keys use NO ACTION: no cascading deletion of users, sessions or audit history.
- `UserName` uses `Latin1_General_100_CI_AS` collation for case-insensitive, accent-sensitive uniqueness and lookup. Leading/trailing whitespace is rejected at the API boundary; no normalized column. UserName permits letters, digits, `_`, `-`, `.`; 1–100 characters.
- `RoleName` uses `Latin1_General_100_BIN2` collation with a unique index. Role checks in application use exact ordinal names, never numeric role ordering.
- Unique indexes on Users.UserName and Roles.RoleName; indexes on Users.RoleId, UserSessions.(UserId, ExpiresAt), AuditLogs.(ActorUserId, CreatedAt).
- Proposed filtered unique index on Users.RoleId for the Admin role is **not** used: its numeric ID is unknown. Account creation rejects Admin server-side. Manual DB administrators must maintain the single-Admin rule.
- No defaults or seed data. Role rows and the single Admin are inserted manually by the owner.
- No check that ExpiresAt must be after LoggedInAt: immediate expiration/logout of a superseded session must remain possible.
- Unknown-user login failures go to application logs with event category only, without submitted username/password; no anonymous AuditLog actor is invented.

## Authentication conventions, independent of new columns

- Login only by UserName/password. Allowed exact names: System Administrator, Restaurant Manager, Waiter, Cashier, Kitchen Staff, Inventory Staff.
- Admin may create only Restaurant Manager, Waiter, Cashier, Kitchen Staff, Inventory Staff.
- JWT duration defaults to 60 minutes; session uses the same expiration, rounded to JWT second precision. Validate issuer, audience, signing key and expiration, with zero clock skew.
- Login holds a SQL Server UPDLOCK on the existing User row until transaction commit. Concurrent logins for the same user serialize across API instances. Only Waiter logins expire previous live sessions before inserting the new session atomically. This adds no schema. Other roles retain multiple sessions.
- Protected requests reload the user, role and session and require current IsActive, matching UserId and unexpired ExpiresAt. JWT role claims do not grant stale permissions.
- Successful login, logout and account creation record sanitized audit events in the same transaction as changes.

If an application database already exists, compare its DDL before applying approved SQL changes. Update SQL first, then scaffold again under review; never run EF migrations or automatic schema creation.
