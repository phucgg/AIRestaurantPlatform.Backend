-- SQLCMD mode in SSMS: Query > SQLCMD Mode. Prepend :setvar IdentityDatabase "Identity"
-- Or: sqlcmd -b -i database/identity.sql -v IdentityDatabase="IdentityTests_ci"
USE [master];
GO
IF DB_ID(N'$(IdentityDatabase)') IS NULL
BEGIN
    CREATE DATABASE [$(IdentityDatabase)];
END;
GO
USE [$(IdentityDatabase)];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Roles') IS NOT NULL OR OBJECT_ID(N'dbo.Users') IS NOT NULL
 OR OBJECT_ID(N'dbo.UserSessions') IS NOT NULL OR OBJECT_ID(N'dbo.AuditLogs') IS NOT NULL
    THROW 51000, 'Identity tables already exist. Compare DDL; this script never replaces existing tables.', 1;

CREATE TABLE dbo.Roles (
    RoleId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    RoleName nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Description nvarchar(500) NULL,
    CONSTRAINT CK_Roles_RoleName CHECK (LEN(RoleName) > 0)
);
CREATE UNIQUE INDEX UX_Roles_RoleName ON dbo.Roles(RoleName);
CREATE TABLE dbo.Users (
    UserId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    RoleId int NOT NULL,
    UserName nvarchar(100) COLLATE Latin1_General_100_CI_AS NOT NULL,
    Email nvarchar(254) NULL,
    PhoneNumber nvarchar(30) NULL,
    FullName nvarchar(200) NOT NULL,
    AvatarUrl nvarchar(2048) NULL,
    PasswordHash nvarchar(512) NOT NULL,
    IsActive bit NOT NULL,
    CreatedAt datetime2(7) NOT NULL,
    UpdatedAt datetime2(7) NULL,
    CONSTRAINT FK_Users_Roles FOREIGN KEY(RoleId) REFERENCES dbo.Roles(RoleId) ON DELETE NO ACTION,
    CONSTRAINT CK_Users_UserName CHECK (LEN(UserName) > 0),
    CONSTRAINT CK_Users_FullName CHECK (LEN(FullName) > 0)
);
CREATE UNIQUE INDEX UX_Users_UserName ON dbo.Users(UserName);
CREATE INDEX IX_Users_RoleId ON dbo.Users(RoleId);
CREATE TABLE dbo.UserSessions (
    UserSessionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserSessions PRIMARY KEY,
    UserId int NOT NULL,
    IpAddress nvarchar(45) NULL,
    UserAgent nvarchar(512) NULL,
    DeviceType nvarchar(50) NULL,
    LoggedInAt datetime2(7) NOT NULL,
    ExpiresAt datetime2(7) NOT NULL,
    CONSTRAINT FK_UserSessions_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
CREATE INDEX IX_UserSessions_UserId_ExpiresAt ON dbo.UserSessions(UserId, ExpiresAt);
CREATE TABLE dbo.AuditLogs (
    AuditLogId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
    ActorUserId int NOT NULL,
    Action nvarchar(100) NOT NULL,
    EntityName nvarchar(100) NOT NULL,
    EntityId nvarchar(128) NULL,
    OldValue nvarchar(max) NULL,
    NewValue nvarchar(max) NULL,
    IpAddress nvarchar(45) NULL,
    CreatedAt datetime2(7) NOT NULL,
    CONSTRAINT FK_AuditLogs_Users FOREIGN KEY(ActorUserId) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
CREATE INDEX IX_AuditLogs_ActorUserId_CreatedAt ON dbo.AuditLogs(ActorUserId, CreatedAt);
COMMIT;
GO
