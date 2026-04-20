using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupAgentTemplatePhase15Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "A2AAuthScheme",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "A2AEndpoint",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "A2ASecretRef",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchetypeId",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionMode",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HooksJson",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelSwitchingJson",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "A2AAuthScheme",
                table: "GroupAgentTemplates");

            migrationBuilder.DropColumn(
                name: "A2AEndpoint",
                table: "GroupAgentTemplates");

            migrationBuilder.DropColumn(
                name: "A2ASecretRef",
                table: "GroupAgentTemplates");

            migrationBuilder.DropColumn(
                name: "ArchetypeId",
                table: "GroupAgentTemplates");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "GroupAgentTemplates");

            migrationBuilder.DropColumn(
                name: "HooksJson",
                table: "GroupAgentTemplates");

            migrationBuilder.DropColumn(
                name: "ModelSwitchingJson",
                table: "GroupAgentTemplates");
        }
    }
}
