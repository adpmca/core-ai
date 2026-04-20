using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConfigFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxContinuations",
                table: "AgentDefinitions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PipelineStagesJson",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StageInstructionsJson",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolFilterJson",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxContinuations",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "PipelineStagesJson",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "StageInstructionsJson",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "ToolFilterJson",
                table: "AgentDefinitions");
        }
    }
}
