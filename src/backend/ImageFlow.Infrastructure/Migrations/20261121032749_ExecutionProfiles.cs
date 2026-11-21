using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstimateConfiguration",
                table: "RuntimeSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdapterVersion",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AttemptLimit",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CredentialReference",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Endpoint",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImageComplexity",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImageQuality",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OutputFormat",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderProfile",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
            // Old jobs have no trustworthy endpoint/options snapshot. Preserve their history,
            // reservations and Unknown attempts, but never infer an executable profile.
            migrationBuilder.Sql("UPDATE Jobs SET Status = 'NeedsAttention', StopReason = 'ExecutionProfileRequired' WHERE Status IN ('Active', 'Paused', 'NeedsAttention')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimateConfiguration",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AdapterVersion",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AttemptLimit",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CredentialReference",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Endpoint",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ImageComplexity",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ImageQuality",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "OutputFormat",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ProviderProfile",
                table: "Jobs");
        }
    }
}
