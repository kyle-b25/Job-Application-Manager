using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAppManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSubmittedItemsWithFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The columns are added first, then backfilled, and only then is the old table
            // dropped - the scaffolded order dropped it before anything could be read out of it.
            migrationBuilder.AddColumn<bool>(
                name: "CoverLetterSubmitted",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ResumeSubmitted",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // SubmissionKind: 0 Resume, 1 CoverLetter, 2 Assessment, 3 Other. The last two have
            // no flag to land in and are dropped with the table - "did I send a resume" is the
            // only question the two booleans can answer.
            migrationBuilder.Sql(
                "UPDATE Applications SET ResumeSubmitted = 1 WHERE Id IN " +
                "(SELECT ApplicationId FROM SubmittedItems WHERE Kind = 0);");

            migrationBuilder.Sql(
                "UPDATE Applications SET CoverLetterSubmitted = 1 WHERE Id IN " +
                "(SELECT ApplicationId FROM SubmittedItems WHERE Kind = 1);");

            migrationBuilder.DropTable(
                name: "SubmittedItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubmittedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SubmittedOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmittedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmittedItems_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubmittedItems_ApplicationId",
                table: "SubmittedItems",
                column: "ApplicationId");

            // One generically-named row per set flag. The specific document names the old table
            // held were dropped by Up and cannot come back; this restores the shape, not the data.
            migrationBuilder.Sql(
                "INSERT INTO SubmittedItems (ApplicationId, Kind, Name, SubmittedOn) " +
                "SELECT Id, 0, 'Resume', DateApplied FROM Applications WHERE ResumeSubmitted = 1;");

            migrationBuilder.Sql(
                "INSERT INTO SubmittedItems (ApplicationId, Kind, Name, SubmittedOn) " +
                "SELECT Id, 1, 'Cover letter', DateApplied FROM Applications WHERE CoverLetterSubmitted = 1;");

            migrationBuilder.DropColumn(
                name: "CoverLetterSubmitted",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ResumeSubmitted",
                table: "Applications");
        }
    }
}
