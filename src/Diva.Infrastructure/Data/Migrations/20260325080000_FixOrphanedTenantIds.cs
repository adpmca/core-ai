using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixOrphanedTenantIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before tenant isolation was enforced on writes, records were saved with TenantId=0.
            // Reassign all such orphaned records to tenant 1 (the first/default tenant).
            // Tables covered: AgentDefinitions, BusinessRules, PromptOverrides,
            //                 LearnedRules, Sessions, ScheduledTasks, ScheduledTaskRuns.
            // Note: SessionMessages has no TenantId column — tenant isolation is via SessionId FK.

            migrationBuilder.Sql(
                "UPDATE AgentDefinitions    SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql(
                "UPDATE BusinessRules       SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql(
                "UPDATE PromptOverrides     SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql(
                "UPDATE LearnedRules        SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql(
                "UPDATE Sessions            SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql(
                "UPDATE ScheduledTasks      SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql(
                "UPDATE ScheduledTaskRuns   SET TenantId = 1 WHERE TenantId = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reliably reverse a data migration — records that were originally
            // tenant 1 are indistinguishable from those reassigned from 0.
        }
    }
}
