
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class UserOnboardingStepConfiguration
    : IEntityTypeConfiguration<UserOnboardingStep>
{
    public void Configure(EntityTypeBuilder<UserOnboardingStep> builder)
    {
        builder.ToTable("user_onboarding_steps", table =>
        {
            table.HasCheckConstraint("ck_user_onboarding_steps_step_number", "step_number BETWEEN 1 AND 7");
        });

        builder.HasKey(x => new
        {
            x.UserId,
            x.StepNumber
        });

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.StepNumber)
            .HasColumnName("step_number")
            .IsRequired();

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany(x => x.OnboardingSteps)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
