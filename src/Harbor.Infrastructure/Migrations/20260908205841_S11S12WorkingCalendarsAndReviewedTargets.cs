using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S11S12WorkingCalendarsAndReviewedTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkingCalendarId",
                table: "SlaPolicies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AppliedWorkingCalendarId",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SlaRecalculationPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ItemsJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CommittedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaRecalculationPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlaRecalculationPlans_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkingCalendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingCalendars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkingCalendars_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlaPolicies_WorkingCalendarId",
                table: "SlaPolicies",
                column: "WorkingCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_AppliedWorkingCalendarId",
                table: "Conversations",
                column: "AppliedWorkingCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_SlaRecalculationPlans_WorkspaceId_CreatedAt",
                table: "SlaRecalculationPlans",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkingCalendars_WorkspaceId_CreatedAt",
                table: "WorkingCalendars",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_WorkingCalendars_AppliedWorkingCalendarId",
                table: "Conversations",
                column: "AppliedWorkingCalendarId",
                principalTable: "WorkingCalendars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SlaPolicies_WorkingCalendars_WorkingCalendarId",
                table: "SlaPolicies",
                column: "WorkingCalendarId",
                principalTable: "WorkingCalendars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_WorkingCalendars_AppliedWorkingCalendarId",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_SlaPolicies_WorkingCalendars_WorkingCalendarId",
                table: "SlaPolicies");

            migrationBuilder.DropTable(
                name: "SlaRecalculationPlans");

            migrationBuilder.DropTable(
                name: "WorkingCalendars");

            migrationBuilder.DropIndex(
                name: "IX_SlaPolicies_WorkingCalendarId",
                table: "SlaPolicies");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_AppliedWorkingCalendarId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "WorkingCalendarId",
                table: "SlaPolicies");

            migrationBuilder.DropColumn(
                name: "AppliedWorkingCalendarId",
                table: "Conversations");
        }
    }
}
