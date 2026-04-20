using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupAgentOverlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupAgentOverlays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    // SQLite forbids function expressions as DEFAULT — EF always provides this from C# entity init.
                    Guid = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupTemplateId = table.Column<string>(type: "TEXT", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SystemPromptAddendum = table.Column<string>(type: "TEXT", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", nullable: true),
                    Temperature = table.Column<double>(type: "REAL", nullable: true),
                    ExtraToolBindingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CustomVariablesJson = table.Column<string>(type: "TEXT", nullable: true),
                    LlmConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupAgentOverlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupAgentOverlays_GroupAgentTemplates_GroupTemplateId",
                        column: x => x.GroupTemplateId,
                        principalTable: "GroupAgentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupAgentOverlays_TenantGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "TenantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupAgentOverlays_GroupId",
                table: "GroupAgentOverlays",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupAgentOverlays_GroupTemplateId",
                table: "GroupAgentOverlays",
                column: "GroupTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupAgentOverlays_Guid",
                table: "GroupAgentOverlays",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupAgentOverlays_TenantId_GroupTemplateId",
                table: "GroupAgentOverlays",
                columns: new[] { "TenantId", "GroupTemplateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupAgentOverlays");
        }
    }
}
