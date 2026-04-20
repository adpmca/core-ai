using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessRuleAgentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentId",
                table: "BusinessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Guid",
                table: "BusinessRules",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // SQLite forbids non-constant ADD COLUMN defaults; populate existing rows now.
            migrationBuilder.Sql(@"
                UPDATE ""BusinessRules""
                SET ""Guid"" = (
                    lower(hex(randomblob(4))) || '-' ||
                    lower(hex(randomblob(2))) || '-4' ||
                    substr(lower(hex(randomblob(2))), 2) || '-' ||
                    substr('89ab', abs(random() % 4) + 1, 1) ||
                    substr(lower(hex(randomblob(2))), 2) || '-' ||
                    lower(hex(randomblob(6)))
                )
                WHERE ""Guid"" = ''
            ");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRules_Guid",
                table: "BusinessRules",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRules_TenantId_AgentId_IsActive",
                table: "BusinessRules",
                columns: new[] { "TenantId", "AgentId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessRules_Guid",
                table: "BusinessRules");

            migrationBuilder.DropIndex(
                name: "IX_BusinessRules_TenantId_AgentId_IsActive",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "BusinessRules");
        }
    }
}
