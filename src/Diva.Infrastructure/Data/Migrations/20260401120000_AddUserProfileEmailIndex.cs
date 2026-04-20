using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileEmailIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enforce one-to-one mapping between SSO email and tenant user.
            // Filtered index excludes empty-string emails so opaque-token users
            // (who may have no email claim) are not blocked.
            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_TenantId_Email",
                table: "UserProfiles",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "\"Email\" != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_TenantId_Email",
                table: "UserProfiles");
        }
    }
}
