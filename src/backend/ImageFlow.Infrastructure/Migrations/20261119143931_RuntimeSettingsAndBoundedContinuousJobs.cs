using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeSettingsAndBoundedContinuousJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BudgetUsd",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostPerImageUsd",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedExposureUsd",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ImageSize",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxImages",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StopReason",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostDisposition",
                table: "Attempts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "Attempts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "RuntimeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaxConcurrency = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedCostPerImageUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeSettings", x => x.Id);
                });
            // Historical work was unpriced. Preserve its mode/count without inventing costs.
            migrationBuilder.Sql("UPDATE Jobs SET Mode = 'Finite', MaxImages = (SELECT COUNT(*) FROM Tasks WHERE Tasks.JobId = Jobs.Id);");
            migrationBuilder.Sql("UPDATE Attempts SET CostDisposition = 'Unpriced';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BudgetUsd",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EstimatedCostPerImageUsd",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EstimatedExposureUsd",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ImageSize",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MaxImages",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "StopReason",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CostDisposition",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "Attempts");
        }
    }
}
