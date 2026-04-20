using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupRuleTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HookPoint",
                table: "GroupBusinessRules",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HookRuleType",
                table: "GroupBusinessRules",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsTemplate",
                table: "GroupBusinessRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxEvaluationMs",
                table: "GroupBusinessRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderInPack",
                table: "GroupBusinessRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "GroupBusinessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Replacement",
                table: "GroupBusinessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StopOnMatch",
                table: "GroupBusinessRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "GroupBusinessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceGroupRuleId",
                table: "BusinessRules",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HookPoint",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "HookRuleType",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "IsTemplate",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "MaxEvaluationMs",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "OrderInPack",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "Replacement",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "StopOnMatch",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "GroupBusinessRules");

            migrationBuilder.DropColumn(
                name: "SourceGroupRuleId",
                table: "BusinessRules");
        }
    }
}
