using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class EditableGenerationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageComplexity",
                table: "RuntimeSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageModel",
                table: "RuntimeSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageQuality",
                table: "RuntimeSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageSize",
                table: "RuntimeSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageComplexity",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ImageModel",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ImageQuality",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ImageSize",
                table: "RuntimeSettings");
        }
    }
}
