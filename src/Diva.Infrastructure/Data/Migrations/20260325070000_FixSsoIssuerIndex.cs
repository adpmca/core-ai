using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixSsoIssuerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the global unique index on Issuer — multiple tenants can share the same IdP.
            migrationBuilder.DropIndex(
                name: "IX_SsoConfigs_Issuer",
                table: "SsoConfigs");

            // Replace with a per-tenant index (non-unique at DB level; uniqueness is enforced per tenant).
            migrationBuilder.CreateIndex(
                name: "IX_SsoConfigs_TenantId_Issuer",
                table: "SsoConfigs",
                columns: new[] { "TenantId", "Issuer" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SsoConfigs_TenantId_Issuer",
                table: "SsoConfigs");

            migrationBuilder.CreateIndex(
                name: "IX_SsoConfigs_Issuer",
                table: "SsoConfigs",
                column: "Issuer",
                unique: true);
        }
    }
}
