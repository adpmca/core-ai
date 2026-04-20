using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase15_CustomAgents_A2A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "A2AAuthScheme",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "A2AEndpoint",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "A2ASecretRef",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchetypeId",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionMode",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HooksJson",
                table: "AgentDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", nullable: true),
                    OutputText = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SessionId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_TenantId_Status",
                table: "AgentTasks",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "A2AAuthScheme",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "A2AEndpoint",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "A2ASecretRef",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "ArchetypeId",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "HooksJson",
                table: "AgentDefinitions");
        }
    }
}
