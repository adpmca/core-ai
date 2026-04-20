using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnsureA2ARemoteAgentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use IF NOT EXISTS to safely add the column regardless of whether
            // the previous migration (20260415214827) applied it fully or not.
            migrationBuilder.Sql(
                "ALTER TABLE AgentDefinitions ADD COLUMN IF NOT EXISTS A2ARemoteAgentId TEXT NULL;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "ALTER TABLE GroupAgentTemplates ADD COLUMN IF NOT EXISTS A2ARemoteAgentId TEXT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
