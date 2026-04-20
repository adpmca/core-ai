using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformLlmConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    DeploymentName = table.Column<string>(type: "TEXT", nullable: true),
                    AvailableModelsJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformLlmConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantLlmConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    DeploymentName = table.Column<string>(type: "TEXT", nullable: true),
                    AvailableModelsJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantLlmConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroupAgentTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    AgentType = table.Column<string>(type: "TEXT", nullable: false),
                    SystemPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", nullable: true),
                    Temperature = table.Column<double>(type: "REAL", nullable: false),
                    MaxIterations = table.Column<int>(type: "INTEGER", nullable: false),
                    Capabilities = table.Column<string>(type: "TEXT", nullable: true),
                    ToolBindings = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationMode = table.Column<string>(type: "TEXT", nullable: true),
                    ContextWindowJson = table.Column<string>(type: "TEXT", nullable: true),
                    CustomVariablesJson = table.Column<string>(type: "TEXT", nullable: true),
                    MaxContinuations = table.Column<int>(type: "INTEGER", nullable: true),
                    PipelineStagesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ToolFilterJson = table.Column<string>(type: "TEXT", nullable: true),
                    StageInstructionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupAgentTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupAgentTemplates_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupBusinessRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentType = table.Column<string>(type: "TEXT", nullable: false),
                    RuleCategory = table.Column<string>(type: "TEXT", nullable: false),
                    RuleKey = table.Column<string>(type: "TEXT", nullable: false),
                    RuleValueJson = table.Column<string>(type: "TEXT", nullable: true),
                    PromptInjection = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupBusinessRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupBusinessRules_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupLlmConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    DeploymentName = table.Column<string>(type: "TEXT", nullable: true),
                    AvailableModelsJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupLlmConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupLlmConfigs_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupPromptOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentType = table.Column<string>(type: "TEXT", nullable: false),
                    Section = table.Column<string>(type: "TEXT", nullable: false),
                    CustomText = table.Column<string>(type: "TEXT", nullable: false),
                    MergeMode = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPromptOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupPromptOverrides_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupScheduledTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentType = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduleType = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RunAtTime = table.Column<string>(type: "TEXT", nullable: true),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadType = table.Column<string>(type: "TEXT", nullable: false),
                    PromptText = table.Column<string>(type: "TEXT", nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupScheduledTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupScheduledTasks_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantGroupMembers_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupScheduledTaskRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    GroupTaskId = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    ResponseText = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SessionId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupScheduledTaskRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupScheduledTaskRuns_GroupScheduledTasks_GroupTaskId",
                        column: x => x.GroupTaskId,
                        principalTable: "GroupScheduledTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupAgentTemplates_GroupId_IsEnabled",
                table: "GroupAgentTemplates",
                columns: new[] { "GroupId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupBusinessRules_GroupId_AgentType_IsActive",
                table: "GroupBusinessRules",
                columns: new[] { "GroupId", "AgentType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupLlmConfigs_GroupId",
                table: "GroupLlmConfigs",
                column: "GroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupPromptOverrides_GroupId_AgentType_IsActive",
                table: "GroupPromptOverrides",
                columns: new[] { "GroupId", "AgentType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupScheduledTaskRuns_GroupTaskId_TenantId_Status",
                table: "GroupScheduledTaskRuns",
                columns: new[] { "GroupTaskId", "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupScheduledTasks_GroupId_IsEnabled_NextRunUtc",
                table: "GroupScheduledTasks",
                columns: new[] { "GroupId", "IsEnabled", "NextRunUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantGroupMembers_GroupId_TenantId",
                table: "TenantGroupMembers",
                columns: new[] { "GroupId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantGroupMembers_TenantId",
                table: "TenantGroupMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLlmConfigs_TenantId",
                table: "TenantLlmConfigs",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupAgentTemplates");

            migrationBuilder.DropTable(
                name: "GroupBusinessRules");

            migrationBuilder.DropTable(
                name: "GroupLlmConfigs");

            migrationBuilder.DropTable(
                name: "GroupPromptOverrides");

            migrationBuilder.DropTable(
                name: "GroupScheduledTaskRuns");

            migrationBuilder.DropTable(
                name: "PlatformLlmConfigs");

            migrationBuilder.DropTable(
                name: "TenantGroupMembers");

            migrationBuilder.DropTable(
                name: "TenantLlmConfigs");

            migrationBuilder.DropTable(
                name: "GroupScheduledTasks");

            migrationBuilder.DropTable(
                name: "TenantGroups");
        }
    }
}
