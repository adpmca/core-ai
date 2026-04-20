using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddA2ARemoteAgentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "A2ARemoteAgentId",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "A2ARemoteAgentId",
                table: "GroupAgentTemplates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "A2ARemoteAgentId",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "A2ARemoteAgentId",
                table: "GroupAgentTemplates");
        }
    }
}
