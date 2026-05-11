using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentOptimizationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FewShotExamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceSessionId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceTurnNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    UserMessage = table.Column<string>(type: "TEXT", nullable: false),
                    AssistantMessage = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FewShotExamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptimizationConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduleType = table.Column<string>(type: "TEXT", nullable: false),
                    RunAtTime = table.Column<string>(type: "TEXT", nullable: true),
                    RunOnDayOfWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    Timezone = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastScheduledRunAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptimizationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TriggerSource = table.Column<string>(type: "TEXT", nullable: false),
                    SessionsAnalyzed = table.Column<int>(type: "INTEGER", nullable: false),
                    TurnsAnalyzed = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    FromDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ToDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptimizationSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    RunId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentValue = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedValue = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<float>(type: "REAL", nullable: false),
                    Reasoning = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OptimizationSuggestions_OptimizationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "OptimizationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FewShotExamples_TenantId_AgentId_SortOrder",
                table: "FewShotExamples",
                columns: new[] { "TenantId", "AgentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationConfigs_TenantId_AgentId",
                table: "OptimizationConfigs",
                columns: new[] { "TenantId", "AgentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationRuns_TenantId_AgentId_StartedAt",
                table: "OptimizationRuns",
                columns: new[] { "TenantId", "AgentId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationSuggestions_RunId",
                table: "OptimizationSuggestions",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationSuggestions_TenantId_AgentId_Status",
                table: "OptimizationSuggestions",
                columns: new[] { "TenantId", "AgentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FewShotExamples");

            migrationBuilder.DropTable(
                name: "OptimizationConfigs");

            migrationBuilder.DropTable(
                name: "OptimizationSuggestions");

            migrationBuilder.DropTable(
                name: "OptimizationRuns");
        }
    }
}
