using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class UniquePlanJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Jobs_PlanId",
                table: "Jobs",
                column: "PlanId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_PlanId",
                table: "Jobs");
        }
    }
}
