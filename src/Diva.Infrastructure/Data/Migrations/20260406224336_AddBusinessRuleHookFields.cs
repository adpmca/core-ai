using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessRuleHookFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BusinessRuleId",
                table: "RuleExecutionLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HookPoint",
                table: "BusinessRules",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HookRuleType",
                table: "BusinessRules",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxEvaluationMs",
                table: "BusinessRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderInPack",
                table: "BusinessRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "BusinessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Replacement",
                table: "BusinessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RulePackId",
                table: "BusinessRules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StopOnMatch",
                table: "BusinessRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "BusinessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRules_RulePackId",
                table: "BusinessRules",
                column: "RulePackId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRules_TenantId_RulePackId",
                table: "BusinessRules",
                columns: new[] { "TenantId", "RulePackId" });

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessRules_RulePacks_RulePackId",
                table: "BusinessRules",
                column: "RulePackId",
                principalTable: "RulePacks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessRules_RulePacks_RulePackId",
                table: "BusinessRules");

            migrationBuilder.DropIndex(
                name: "IX_BusinessRules_RulePackId",
                table: "BusinessRules");

            migrationBuilder.DropIndex(
                name: "IX_BusinessRules_TenantId_RulePackId",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "BusinessRuleId",
                table: "RuleExecutionLogs");

            migrationBuilder.DropColumn(
                name: "HookPoint",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "HookRuleType",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "MaxEvaluationMs",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "OrderInPack",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "Replacement",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "RulePackId",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "StopOnMatch",
                table: "BusinessRules");

            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "BusinessRules");
        }
    }
}
