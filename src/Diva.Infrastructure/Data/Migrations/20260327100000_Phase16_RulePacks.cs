using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase16_RulePacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RulePacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMandatory = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppliesToJson = table.Column<string>(type: "TEXT", nullable: true),
                    ActivationCondition = table.Column<string>(type: "TEXT", nullable: true),
                    ParentPackId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxEvaluationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RulePacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RulePacks_RulePacks_ParentPackId",
                        column: x => x.ParentPackId,
                        principalTable: "RulePacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RulePacks_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HookRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackId = table.Column<int>(type: "INTEGER", nullable: false),
                    HookPoint = table.Column<string>(type: "TEXT", nullable: false),
                    RuleType = table.Column<string>(type: "TEXT", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", nullable: true),
                    Instruction = table.Column<string>(type: "TEXT", nullable: true),
                    Replacement = table.Column<string>(type: "TEXT", nullable: true),
                    ToolName = table.Column<string>(type: "TEXT", nullable: true),
                    OrderInPack = table.Column<int>(type: "INTEGER", nullable: false),
                    StopOnMatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    OverridesParentRuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxEvaluationMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HookRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HookRules_HookRules_OverridesParentRuleId",
                        column: x => x.OverridesParentRuleId,
                        principalTable: "HookRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HookRules_RulePacks_PackId",
                        column: x => x.PackId,
                        principalTable: "RulePacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackId = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Triggered = table.Column<bool>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ElapsedMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RulePacks_TenantId_IsEnabled_Priority",
                table: "RulePacks",
                columns: new[] { "TenantId", "IsEnabled", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_RulePacks_GroupId",
                table: "RulePacks",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RulePacks_ParentPackId",
                table: "RulePacks",
                column: "ParentPackId");

            migrationBuilder.CreateIndex(
                name: "IX_HookRules_PackId_OrderInPack",
                table: "HookRules",
                columns: new[] { "PackId", "OrderInPack" });

            migrationBuilder.CreateIndex(
                name: "IX_HookRules_OverridesParentRuleId",
                table: "HookRules",
                column: "OverridesParentRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleExecutionLogs_TenantId_Timestamp",
                table: "RuleExecutionLogs",
                columns: new[] { "TenantId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleExecutionLogs_PackId_RuleId_Timestamp",
                table: "RuleExecutionLogs",
                columns: new[] { "PackId", "RuleId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RuleExecutionLogs");
            migrationBuilder.DropTable(name: "HookRules");
            migrationBuilder.DropTable(name: "RulePacks");
        }
    }
}
