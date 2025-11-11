using Microsoft.EntityFrameworkCore;
using NSerf.Lighthouse.Data.Entities;

namespace NSerf.Lighthouse.Data;

public class LighthouseDbContext(DbContextOptions<LighthouseDbContext> options) : DbContext(options)
{
    public DbSet<ClusterEntity> Clusters { get; set; } = null!;
    public DbSet<NodeEntity> Nodes { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClusterEntity>(entity =>
        {
            entity.ToTable("clusters");
            entity.HasKey(e => e.ClusterId);
            entity.Property(e => e.ClusterId).HasColumnName("cluster_id");
            entity.Property(e => e.PublicKey).HasColumnName("public_key").IsRequired();
            entity.HasIndex(e => e.ClusterId).IsUnique();
        });

        modelBuilder.Entity<NodeEntity>(entity =>
        {
            entity.ToTable("nodes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ClusterId).HasColumnName("cluster_id").IsRequired();
            entity.Property(e => e.VersionName).HasColumnName("version_name").IsRequired().HasMaxLength(255);
            entity.Property(e => e.VersionNumber).HasColumnName("version_number").IsRequired();
            entity.Property(e => e.EncryptedPayload).HasColumnName("encrypted_payload").IsRequired();
            entity.Property(e => e.ServerTimeStamp).HasColumnName("server_timestamp").IsRequired();
            entity.HasIndex(e => new { e.ClusterId, e.VersionName, e.VersionNumber, e.ServerTimeStamp })
                .HasDatabaseName("idx_nodes_cluster_version_timestamp");
            entity.HasIndex(e => e.ServerTimeStamp)
                .HasDatabaseName("idx_nodes_timestamp");
        });
    }
}
