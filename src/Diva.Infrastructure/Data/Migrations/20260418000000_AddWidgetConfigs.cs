using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WidgetConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedOriginsJson = table.Column<string>(type: "TEXT", nullable: true),
                    SsoConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    AllowAnonymous = table.Column<bool>(type: "INTEGER", nullable: false),
                    WelcomeMessage = table.Column<string>(type: "TEXT", nullable: true),
                    PlaceholderText = table.Column<string>(type: "TEXT", nullable: true),
                    ThemeJson = table.Column<string>(type: "TEXT", nullable: true),
                    RespectSystemTheme = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowBranding = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WidgetConfigs_TenantId_IsActive",
                table: "WidgetConfigs",
                columns: new[] { "TenantId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WidgetConfigs");
        }
    }
}
