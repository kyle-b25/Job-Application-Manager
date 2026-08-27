using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAppManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSalaryRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-way: Down puts the column back, but nothing can put the values back.
            migrationBuilder.DropColumn(
                name: "SalaryRange",
                table: "Applications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SalaryRange",
                table: "Applications",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }
    }
}
