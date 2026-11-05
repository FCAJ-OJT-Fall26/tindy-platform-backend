using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;
namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ChatRequestConfiguration : IEntityTypeConfiguration<ChatRequest>
{
    public void Configure(EntityTypeBuilder<ChatRequest> b)
    {
        b.ToTable("chat_requests", t =>
        {
            t.HasCheckConstraint("ck_chat_requests_status", "status IN ('pending','accepted','rejected','cancelled','expired')");
            t.HasCheckConstraint("ck_chat_requests_parties", "sender_id <> recipient_id");
            t.HasCheckConstraint("ck_chat_requests_body", "char_length(body) BETWEEN 1 AND 500");
        });
        b.HasKey(x => x.RequestId);
        foreach (var p in b.Metadata.GetProperties()) p.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name));
        b.Property(x => x.PairKey).HasMaxLength(65);
        b.Property(x => x.Body).HasMaxLength(500);
        b.Property(x => x.Kind).HasMaxLength(30);
        b.Property(x => x.Status).HasMaxLength(20);
        b.HasIndex(x => x.PairKey).IsUnique().HasFilter("status = 'pending'");
        b.HasIndex(x => new { x.SenderId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.SenderId, x.CreatedAt });
        b.HasIndex(x => new { x.RecipientId, x.CreatedAt });
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => x.PositionId);
        b.HasIndex(x => x.ApplicationId);
        // Context IDs are snapshots so project deletion preserves request history.
    }
}

public sealed class ChatBlockConfiguration : IEntityTypeConfiguration<ChatBlock>
{
    public void Configure(EntityTypeBuilder<ChatBlock> b)
    {
        b.ToTable("chat_blocks", t => t.HasCheckConstraint("ck_chat_blocks_parties", "blocker_id <> blocked_id"));
        b.HasKey(x => new { x.BlockerId, x.BlockedId });
        foreach (var p in b.Metadata.GetProperties()) p.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name));
        b.Property(x => x.PairKey).HasMaxLength(65);
        b.HasIndex(x => x.PairKey);
    }
}
