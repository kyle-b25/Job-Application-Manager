using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAppManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyStatusPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "InterviewDate",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            // ApplicationStatus went from seven stages to three, renumbered from zero, and it is
            // stored as an INTEGER - so existing rows hold the old ordinals. A single CASE per
            // table rather than a chain of UPDATEs: the old and new ranges overlap, so a chain
            // would rewrite the same row twice.
            //
            //   0 Wishlist    1 Applied   2 PhoneScreen -> 0 Applied
            //   3 Interview   4 Offer                   -> 1 Interview
            //   5 Rejected    6 Withdrawn               -> 2 Rejected
            const string remap =
                "SET Status = CASE Status " +
                "WHEN 3 THEN 1 WHEN 4 THEN 1 " +
                "WHEN 5 THEN 2 WHEN 6 THEN 2 " +
                "ELSE 0 END;";

            migrationBuilder.Sql("UPDATE Applications " + remap);
            migrationBuilder.Sql("UPDATE StatusChanges " + remap);

            // Collapsing seven stages into three turns some real transitions into no-ops - a row
            // that went Wishlist -> Applied is now Applied -> Applied. Left in place those become
            // zero-length stints and drag the average-days-in-stage figures toward zero, so drop
            // any entry whose status matches the one immediately before it for the same
            // application. This is the same "re-entering a status is not a transition" rule
            // ChangeStatusAsync enforces on new writes.
            migrationBuilder.Sql(
                "DELETE FROM StatusChanges WHERE Id IN (" +
                "  SELECT s.Id FROM StatusChanges s" +
                "  WHERE s.Status = (" +
                "    SELECT p.Status FROM StatusChanges p" +
                "    WHERE p.ApplicationId = s.ApplicationId" +
                "      AND (p.ChangedUtc, p.Id) < (s.ChangedUtc, s.Id)" +
                "    ORDER BY p.ChangedUtc DESC, p.Id DESC LIMIT 1));");

            // The interview date is new, so nothing can be backfilled into it, but an application
            // sitting outside Interview must not carry one - the same rule the repository applies.
            migrationBuilder.Sql("UPDATE Applications SET InterviewRound = NULL WHERE Status <> 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversed onto the nearest old ordinal - Applied, Interview, Rejected. The four
            // stages that were merged away cannot come back, and the history entries dropped
            // above are gone for good; this is a one-way narrowing of the data.
            const string unmap =
                "SET Status = CASE Status WHEN 0 THEN 1 WHEN 1 THEN 3 WHEN 2 THEN 5 ELSE 1 END;";

            migrationBuilder.Sql("UPDATE StatusChanges " + unmap);
            migrationBuilder.Sql("UPDATE Applications " + unmap);

            migrationBuilder.DropColumn(
                name: "InterviewDate",
                table: "Applications");
        }
    }
}
