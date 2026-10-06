using System;
using System.Collections.Generic;
using IdentityService.API.Models;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.API.Data;

public partial class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserSession> UserSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => new { e.ActorUserId, e.CreatedAt }, "IX_AuditLogs_ActorUserId_CreatedAt");

            entity.Property(e => e.Action).HasMaxLength(100);
            entity.Property(e => e.EntityId).HasMaxLength(128);
            entity.Property(e => e.EntityName).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(45);

            entity.HasOne(d => d.ActorUser).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.ActorUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AuditLogs_Users");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(e => e.RoleName, "UX_Roles_RoleName").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.RoleName)
                .HasMaxLength(100)
                .UseCollation("Latin1_General_100_BIN2");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.RoleId, "IX_Users_RoleId");

            entity.HasIndex(e => e.UserName, "UX_Users_UserName").IsUnique();

            entity.Property(e => e.AvatarUrl).HasMaxLength(2048);
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.PasswordHash).HasMaxLength(512);
            entity.Property(e => e.PhoneNumber).HasMaxLength(30);
            entity.Property(e => e.UserName)
                .HasMaxLength(100)
                .UseCollation("Latin1_General_100_CI_AS");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Users_Roles");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.ExpiresAt }, "IX_UserSessions_UserId_ExpiresAt");

            entity.Property(e => e.DeviceType).HasMaxLength(50);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);

            entity.HasOne(d => d.User).WithMany(p => p.UserSessions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserSessions_Users");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
