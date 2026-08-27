using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAppManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusHistoryAndPipelineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JobUrl",
                table: "Applications",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalaryRange",
                table: "Applications",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StatusChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatusChanges_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatusChanges_ApplicationId_ChangedUtc",
                table: "StatusChanges",
                columns: new[] { "ApplicationId", "ChangedUtc" });

            // The ApplicationStatus enum went from 4 members to a 7-stage pipeline, and it is
            // stored as an INTEGER, so existing rows hold the old ordinals. Remapped in
            // descending order of the *old* value so a rewritten row is never picked up again
            // by a later statement.
            migrationBuilder.Sql("UPDATE Applications SET Status = 4 WHERE Status = 3;"); // Accepted   -> Offer
            migrationBuilder.Sql("UPDATE Applications SET Status = 5 WHERE Status = 2;"); // Rejected   -> Rejected
            migrationBuilder.Sql("UPDATE Applications SET Status = 3 WHERE Status = 1;"); // Interview  -> Interview
            migrationBuilder.Sql("UPDATE Applications SET Status = 1 WHERE Status = 0;"); // NoResponse -> Applied

            // Give every pre-existing application a history so nothing shows an empty timeline.
            // CreatedUtc is already stored in the same TEXT format ChangedUtc uses.
            migrationBuilder.Sql(
                "INSERT INTO StatusChanges (ApplicationId, Status, ChangedUtc, Note) " +
                "SELECT Id, Status, CreatedUtc, 'Backfilled from the pre-history record.' " +
                "FROM Applications;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the remap, ascending this time for the same non-collision reason.
            // Stages with no old equivalent collapse onto the nearest one that had one.
            migrationBuilder.Sql("UPDATE Applications SET Status = 0 WHERE Status = 0;"); // Wishlist    -> NoResponse
            migrationBuilder.Sql("UPDATE Applications SET Status = 0 WHERE Status = 1;"); // Applied     -> NoResponse
            migrationBuilder.Sql("UPDATE Applications SET Status = 1 WHERE Status = 2;"); // PhoneScreen -> Interview
            migrationBuilder.Sql("UPDATE Applications SET Status = 1 WHERE Status = 3;"); // Interview   -> Interview
            migrationBuilder.Sql("UPDATE Applications SET Status = 3 WHERE Status = 4;"); // Offer       -> Accepted
            migrationBuilder.Sql("UPDATE Applications SET Status = 2 WHERE Status = 5;"); // Rejected    -> Rejected
            migrationBuilder.Sql("UPDATE Applications SET Status = 2 WHERE Status = 6;"); // Withdrawn   -> Rejected

            migrationBuilder.DropTable(
                name: "StatusChanges");

            migrationBuilder.DropColumn(
                name: "JobUrl",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "SalaryRange",
                table: "Applications");
        }
    }
}
