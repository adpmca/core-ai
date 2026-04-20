using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds Name to PlatformLlmConfigs (multi-config catalog), PlatformConfigRef FK to GroupLlmConfigs,
    /// LlmConfigId to AgentDefinitions/GroupAgentTemplates, and Name to TenantLlmConfigs.
    /// Incorporates the operations from the deleted AddAgentLevelLlmConfig migration.
    /// </summary>
    public partial class AddLlmConfigCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── AgentDefinitions / GroupAgentTemplates: add LlmConfigId ──────────
            migrationBuilder.AddColumn<int>(
                name: "LlmConfigId",
                table: "GroupAgentTemplates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LlmConfigId",
                table: "AgentDefinitions",
                type: "INTEGER",
                nullable: true);

            // ── TenantLlmConfigs: add Name column and named index ─────────────────
            migrationBuilder.DropIndex(
                name: "IX_TenantLlmConfigs_TenantId",
                table: "TenantLlmConfigs");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "TenantLlmConfigs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantLlmConfigs_TenantId_Name",
                table: "TenantLlmConfigs",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "[Name] IS NOT NULL");

            // ── PlatformLlmConfigs: add Name column ───────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PlatformLlmConfigs",
                type: "TEXT",
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformLlmConfigs_Name",
                table: "PlatformLlmConfigs",
                column: "Name",
                unique: true);

            // ── GroupLlmConfigs: add Name column (was in deleted AddAgentLevelLlmConfig migration) ──
            // Use raw SQL so this is a no-op on existing databases that already have the column.
            migrationBuilder.Sql(@"
                ALTER TABLE GroupLlmConfigs ADD COLUMN Name TEXT;
            ");

            // ── GroupLlmConfigs: add PlatformConfigRef FK ─────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "PlatformConfigRef",
                table: "GroupLlmConfigs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupLlmConfigs_PlatformConfigRef",
                table: "GroupLlmConfigs",
                column: "PlatformConfigRef");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupLlmConfigs_PlatformLlmConfigs_PlatformConfigRef",
                table: "GroupLlmConfigs",
                column: "PlatformConfigRef",
                principalTable: "PlatformLlmConfigs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Data: rename unnamed group configs to 'Default' ───────────────────
            migrationBuilder.Sql("UPDATE GroupLlmConfigs SET Name = 'Default' WHERE Name IS NULL");

            // ── GroupLlmConfigs: replace partial index with full unique index ──────
            // DROP INDEX IF EXISTS handles both fresh databases and those with a prior partial index.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_GroupLlmConfigs_GroupId_Name""");

            migrationBuilder.CreateIndex(
                name: "IX_GroupLlmConfigs_GroupId_Name",
                table: "GroupLlmConfigs",
                columns: new[] { "GroupId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupLlmConfigs_PlatformLlmConfigs_PlatformConfigRef",
                table: "GroupLlmConfigs");

            migrationBuilder.DropIndex(
                name: "IX_GroupLlmConfigs_PlatformConfigRef",
                table: "GroupLlmConfigs");

            migrationBuilder.DropIndex(
                name: "IX_GroupLlmConfigs_GroupId_Name",
                table: "GroupLlmConfigs");

            migrationBuilder.DropIndex(
                name: "IX_PlatformLlmConfigs_Name",
                table: "PlatformLlmConfigs");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_TenantLlmConfigs_TenantId_Name""");

            migrationBuilder.DropColumn(name: "LlmConfigId", table: "GroupAgentTemplates");
            migrationBuilder.DropColumn(name: "LlmConfigId", table: "AgentDefinitions");
            migrationBuilder.DropColumn(name: "Name",        table: "TenantLlmConfigs");
            migrationBuilder.DropColumn(name: "PlatformConfigRef", table: "GroupLlmConfigs");
            migrationBuilder.DropColumn(name: "Name",        table: "PlatformLlmConfigs");

            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TenantLlmConfigs_TenantId"" ON ""TenantLlmConfigs""(""TenantId"")");

            migrationBuilder.CreateIndex(
                name: "IX_GroupLlmConfigs_GroupId_Name",
                table: "GroupLlmConfigs",
                columns: new[] { "GroupId", "Name" },
                unique: true,
                filter: "[Name] IS NOT NULL");
        }
    }
}
