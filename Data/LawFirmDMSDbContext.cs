using Microsoft.EntityFrameworkCore;
using CKNDocument.Models.LawFirmDMS;

namespace CKNDocument.Data;

/// <summary>
/// Unified Database context for LawFirmDMS database
/// Manages: All entities including SuperAdmin, Firm subscriptions, Invoices, Payments, Revenue, Expenses
/// This is now the single database context (merged from OwnerERP and LawFirmDMS)
/// </summary>
public class LawFirmDMSDbContext : DbContext
{
      public LawFirmDMSDbContext(DbContextOptions<LawFirmDMSDbContext> options) : base(options)
      {
      }

      // ==========================================
      // Platform/SuperAdmin DbSets (Merged from OwnerERP)
      // ==========================================
      public DbSet<SuperAdmin> SuperAdmins { get; set; } = null!;
      public DbSet<FirmSubscription> FirmSubscriptions { get; set; } = null!;
      public DbSet<Invoice> Invoices { get; set; } = null!;
      public DbSet<InvoiceItem> InvoiceItems { get; set; } = null!;
      public DbSet<Payment> Payments { get; set; } = null!;
      public DbSet<Revenue> Revenues { get; set; } = null!;
      public DbSet<Expense> Expenses { get; set; } = null!;

      // ==========================================
      // Law Firm DbSets (Original LawFirmDMS)
      // ==========================================
      public DbSet<Firm> Firms { get; set; } = null!;
      public DbSet<User> Users { get; set; } = null!;
      public DbSet<Role> Roles { get; set; } = null!;
      public DbSet<UserRole> UserRoles { get; set; } = null!;
      public DbSet<Document> Documents { get; set; } = null!;
      public DbSet<DocumentVersion> DocumentVersions { get; set; } = null!;
      public DbSet<DocumentSignature> DocumentSignatures { get; set; } = null!;
      public DbSet<DocumentReview> DocumentReviews { get; set; } = null!;
      public DbSet<DocumentAccess> DocumentAccesses { get; set; } = null!;
      public DbSet<DocumentRetention> DocumentRetentions { get; set; } = null!;
      public DbSet<RetentionPolicy> RetentionPolicies { get; set; } = null!;
      public DbSet<Archive> Archives { get; set; } = null!;
      public DbSet<AuditLog> AuditLogs { get; set; } = null!;
      public DbSet<Notification> Notifications { get; set; } = null!;
      public DbSet<ClientFolder> ClientFolders { get; set; } = null!;
      public DbSet<Report> Reports { get; set; } = null!;
      public DbSet<DocumentChecklistItem> DocumentChecklistItems { get; set; } = null!;
      public DbSet<DocumentChecklistResult> DocumentChecklistResults { get; set; } = null!;

      protected override void OnModelCreating(ModelBuilder modelBuilder)
      {
            base.OnModelCreating(modelBuilder);

            // ==========================================
            // Platform/SuperAdmin Configuration (Merged from OwnerERP)
            // ==========================================

            // SuperAdmin configuration
            modelBuilder.Entity<SuperAdmin>(entity =>
            {
                  entity.HasIndex(e => e.Username).IsUnique();
                  entity.HasIndex(e => e.Email).IsUnique();
                  entity.Property(e => e.Status).HasDefaultValue("Active");
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
            });

            // FirmSubscription configuration
            modelBuilder.Entity<FirmSubscription>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.Status).HasDefaultValue("Active");

                  entity.HasOne(s => s.Firm)
                    .WithMany(f => f.Subscriptions)
                    .HasForeignKey(s => s.FirmID)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasMany(s => s.Invoices)
                    .WithOne(i => i.Subscription)
                    .HasForeignKey(i => i.SubscriptionID);

                  entity.HasMany(s => s.Payments)
                    .WithOne(p => p.Subscription)
                    .HasForeignKey(p => p.SubscriptionID);

                  entity.HasMany(s => s.Revenues)
                    .WithOne(r => r.Subscription)
                    .HasForeignKey(r => r.SubscriptionID);
            });

            // Invoice configuration
            modelBuilder.Entity<Invoice>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.Status).HasDefaultValue("Pending");

                  entity.HasMany(i => i.InvoiceItems)
                    .WithOne(ii => ii.Invoice)
                    .HasForeignKey(ii => ii.InvoiceID);

                  entity.HasMany(i => i.Payments)
                    .WithOne(p => p.Invoice)
                    .HasForeignKey(p => p.InvoiceID);
            });

            // InvoiceItem configuration
            modelBuilder.Entity<InvoiceItem>(entity =>
            {
                  entity.Property(e => e.Quantity).HasDefaultValue(1);
            });

            // Payment configuration
            modelBuilder.Entity<Payment>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.Status).HasDefaultValue("Pending");
            });

            // Revenue configuration
            modelBuilder.Entity<Revenue>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
            });

            // Expense configuration
            modelBuilder.Entity<Expense>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.Status).HasDefaultValue("Pending");
            });

            // ==========================================
            // Law Firm Configuration (Original LawFirmDMS)
            // ==========================================

            // Firm configuration
            modelBuilder.Entity<Firm>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.Status).HasDefaultValue("Active");
            });

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                  entity.HasIndex(e => e.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
                  entity.HasIndex(e => e.Username).IsUnique().HasFilter("[Username] IS NOT NULL");
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.FailedLoginAttempts).HasDefaultValue(0);
                  entity.Property(e => e.EmailConfirmed).HasDefaultValue(false);

                  entity.HasOne(u => u.Firm)
                    .WithMany(f => f.Users)
                    .HasForeignKey(u => u.FirmID);
            });

            // UserRole configuration
            modelBuilder.Entity<UserRole>(entity =>
            {
                  entity.Property(e => e.AssignedAt).HasDefaultValueSql("GETDATE()");
            });

            // Document configuration
            modelBuilder.Entity<Document>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.WorkflowStage).HasDefaultValue("ClientUpload");
                  entity.Property(e => e.CurrentVersion).HasDefaultValue(1);
                  entity.Property(e => e.IsAIProcessed).HasDefaultValue(false);
                  entity.Property(e => e.IsDuplicate).HasDefaultValue(false);

                  entity.HasOne(d => d.Firm)
                    .WithMany(f => f.Documents)
                    .HasForeignKey(d => d.FirmID);

                  entity.HasOne(d => d.Uploader)
                    .WithMany(u => u.UploadedDocuments)
                    .HasForeignKey(d => d.UploadedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasOne(d => d.AssignedStaff)
                    .WithMany(u => u.AssignedStaffDocuments)
                    .HasForeignKey(d => d.AssignedStaffId)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasOne(d => d.AssignedAdmin)
                    .WithMany(u => u.AssignedAdminDocuments)
                    .HasForeignKey(d => d.AssignedAdminId)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasOne(d => d.DuplicateOfDocument)
                    .WithMany()
                    .HasForeignKey(d => d.DuplicateOfDocumentId)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasOne(d => d.Folder)
                    .WithMany(f => f.Documents)
                    .HasForeignKey(d => d.FolderId);
            });

            // DocumentVersion configuration
            modelBuilder.Entity<DocumentVersion>(entity =>
            {
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.VersionNumber).HasDefaultValue(1);
                  entity.Property(e => e.IsCurrentVersion).HasDefaultValue(false);

                  entity.HasOne(v => v.Document)
                    .WithMany(d => d.Versions)
                    .HasForeignKey(v => v.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);

                  entity.HasOne(v => v.Uploader)
                    .WithMany(u => u.DocumentVersions)
                    .HasForeignKey(v => v.UploadedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // DocumentSignature configuration
            modelBuilder.Entity<DocumentSignature>(entity =>
            {
                  entity.HasIndex(e => e.FileHash);
                  entity.HasIndex(e => e.ContentHash);
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.HasDigitalSignature).HasDefaultValue(false);
                  entity.Property(e => e.IsVerified).HasDefaultValue(false);

                  entity.HasOne(s => s.Document)
                    .WithMany(d => d.Signatures)
                    .HasForeignKey(s => s.DocumentId)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasOne(s => s.Version)
                    .WithMany(v => v.Signatures)
                    .HasForeignKey(s => s.VersionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // DocumentReview configuration
            modelBuilder.Entity<DocumentReview>(entity =>
            {
                  entity.HasIndex(e => e.DocumentId);
                  entity.HasIndex(e => e.ReviewedBy);
                  entity.Property(e => e.ReviewedAt).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.IsChecklistComplete).HasDefaultValue(false);

                  entity.HasOne(r => r.Document)
                    .WithMany(d => d.Reviews)
                    .HasForeignKey(r => r.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);

                  entity.HasOne(r => r.Reviewer)
                    .WithMany(u => u.DocumentReviews)
                    .HasForeignKey(r => r.ReviewedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // DocumentAccess configuration
            modelBuilder.Entity<DocumentAccess>(entity =>
            {
                  entity.Property(e => e.GrantedAt).HasDefaultValueSql("GETDATE()");

                  entity.HasOne(a => a.Document)
                    .WithMany(d => d.Accesses)
                    .HasForeignKey(a => a.DocumentID);

                  entity.HasOne(a => a.User)
                    .WithMany(u => u.DocumentAccesses)
                    .HasForeignKey(a => a.UserID);
            });

            // DocumentRetention configuration
            modelBuilder.Entity<DocumentRetention>(entity =>
            {
                  entity.Property(e => e.IsArchived).HasDefaultValue(false);

                  entity.HasOne(r => r.Document)
                    .WithMany(d => d.Retentions)
                    .HasForeignKey(r => r.DocumentID);

                  entity.HasOne(r => r.Policy)
                    .WithMany(p => p.DocumentRetentions)
                    .HasForeignKey(r => r.PolicyID);
            });

            // RetentionPolicy configuration
            modelBuilder.Entity<RetentionPolicy>(entity =>
            {
                  entity.HasOne(p => p.Creator)
                    .WithMany(u => u.RetentionPolicies)
                    .HasForeignKey(p => p.CreatedBy);
            });

            // Archive configuration
            modelBuilder.Entity<Archive>(entity =>
            {
                  entity.Property(e => e.ArchivedDate).HasDefaultValueSql("GETDATE()");
                  entity.Property(e => e.IsRestored).HasDefaultValue(false);

                  entity.HasOne(a => a.Document)
                    .WithMany(d => d.Archives)
                    .HasForeignKey(a => a.DocumentID);
            });

            // AuditLog configuration
            modelBuilder.Entity<AuditLog>(entity =>
            {
                  entity.HasIndex(e => e.UserID);
                  entity.HasIndex(e => e.SuperAdminId);
                  entity.HasIndex(e => e.FirmID);
                  entity.HasIndex(e => e.Timestamp);
                  entity.HasIndex(e => e.Action);
                  entity.HasIndex(e => e.ActionCategory);
                  entity.Property(e => e.Timestamp).HasDefaultValueSql("GETDATE()");

                  entity.HasOne(a => a.User)
                    .WithMany(u => u.AuditLogs)
                    .HasForeignKey(a => a.UserID)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasOne(a => a.SuperAdmin)
                    .WithMany()
                    .HasForeignKey(a => a.SuperAdminId)
                    .OnDelete(DeleteBehavior.Restrict);

                  entity.HasOne(a => a.Firm)
                    .WithMany()
                    .HasForeignKey(a => a.FirmID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Notification configuration
            modelBuilder.Entity<Notification>(entity =>
            {
                  entity.HasIndex(e => e.UserId);
                  entity.HasIndex(e => e.IsRead);
                  entity.Property(e => e.IsRead).HasDefaultValue(false);
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");

                  entity.HasOne(n => n.User)
                    .WithMany(u => u.Notifications)
                    .HasForeignKey(n => n.UserId);

                  entity.HasOne(n => n.Document)
                    .WithMany(d => d.Notifications)
                    .HasForeignKey(n => n.DocumentId);
            });

            // ClientFolder configuration
            modelBuilder.Entity<ClientFolder>(entity =>
            {
                  entity.HasIndex(e => e.ClientId);
                  entity.HasIndex(e => e.FirmId);
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");

                  entity.HasOne(f => f.Client)
                    .WithMany(u => u.ClientFolders)
                    .HasForeignKey(f => f.ClientId);

                  entity.HasOne(f => f.Firm)
                    .WithMany(firm => firm.ClientFolders)
                    .HasForeignKey(f => f.FirmId);

                  entity.HasOne(f => f.ParentFolder)
                    .WithMany(pf => pf.ChildFolders)
                    .HasForeignKey(f => f.ParentFolderId);
            });

            // Report configuration
            modelBuilder.Entity<Report>(entity =>
            {
                  entity.Property(e => e.GeneratedAt).HasDefaultValueSql("GETDATE()");

                  entity.HasOne(r => r.Generator)
                    .WithMany(u => u.Reports)
                    .HasForeignKey(r => r.GeneratedBy);
            });

            // DocumentChecklistItem configuration
            modelBuilder.Entity<DocumentChecklistItem>(entity =>
            {
                  entity.Property(e => e.IsRequired).HasDefaultValue(true);
                  entity.Property(e => e.DisplayOrder).HasDefaultValue(0);
                  entity.Property(e => e.IsActive).HasDefaultValue(true);
                  entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");

                  entity.HasOne(i => i.Firm)
                    .WithMany(f => f.ChecklistItems)
                    .HasForeignKey(i => i.FirmId);
            });

            // DocumentChecklistResult configuration
            modelBuilder.Entity<DocumentChecklistResult>(entity =>
            {
                  entity.Property(e => e.IsPassed).HasDefaultValue(false);
                  entity.Property(e => e.CheckedAt).HasDefaultValueSql("GETDATE()");

                  entity.HasOne(r => r.Review)
                    .WithMany(rev => rev.ChecklistResults)
                    .HasForeignKey(r => r.ReviewId);

                  entity.HasOne(r => r.ChecklistItem)
                    .WithMany(i => i.Results)
                    .HasForeignKey(r => r.ChecklistItemId);
            });
      }
}
