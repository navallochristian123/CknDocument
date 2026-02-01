using Microsoft.EntityFrameworkCore;
using CKNDocument.Models.OwnerERP;

namespace CKNDocument.Data;

/// <summary>
/// Database context for OwnerERP database
/// Manages: SuperAdmins, Law Firm subscriptions, Invoices, Payments, Revenue, Expenses
/// </summary>
public class OwnerERPDbContext : DbContext
{
    public OwnerERPDbContext(DbContextOptions<OwnerERPDbContext> options) : base(options)
    {
    }

    // DbSets
    public DbSet<SuperAdmin> SuperAdmins { get; set; } = null!;
    public DbSet<Client> Clients { get; set; } = null!;
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceItem> InvoiceItems { get; set; } = null!;
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<Revenue> Revenues { get; set; } = null!;
    public DbSet<Expense> Expenses { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SuperAdmin configuration
        modelBuilder.Entity<SuperAdmin>(entity =>
        {
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Status).HasDefaultValue("Active");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
        });

        // Client configuration
        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasMany(c => c.Invoices)
                  .WithOne(i => i.Client)
                  .HasForeignKey(i => i.ClientID);

            entity.HasMany(c => c.Payments)
                  .WithOne(p => p.Client)
                  .HasForeignKey(p => p.ClientID);

            entity.HasMany(c => c.Revenues)
                  .WithOne(r => r.Client)
                  .HasForeignKey(r => r.ClientID);
        });

        // Invoice configuration
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasMany(i => i.InvoiceItems)
                  .WithOne(ii => ii.Invoice)
                  .HasForeignKey(ii => ii.InvoiceID);

            entity.HasMany(i => i.Payments)
                  .WithOne(p => p.Invoice)
                  .HasForeignKey(p => p.InvoiceID);
        });
    }
}
