using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S14S15S16DurableEventsExportsAndConfigurationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelatedTeammateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityKind = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedFieldsJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AllowedChangesJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfigurationAuditEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    FilterJson = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    CapturedSegmentRulesJson = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SnapshotAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    Generation = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: true),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationExportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationExportJobs_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceEvents",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RecordedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceEvents", x => x.Sequence);
                    table.ForeignKey(
                        name: "FK_WorkspaceEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceEventWatermarks",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurgedThrough = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceEventWatermarks", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_WorkspaceEventWatermarks_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationAuditEvents_WorkspaceId_ActorTeammateId",
                table: "ConfigurationAuditEvents",
                columns: new[] { "WorkspaceId", "ActorTeammateId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationAuditEvents_WorkspaceId_EntityKind_EntityId",
                table: "ConfigurationAuditEvents",
                columns: new[] { "WorkspaceId", "EntityKind", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationAuditEvents_WorkspaceId_OccurredAt_Id",
                table: "ConfigurationAuditEvents",
                columns: new[] { "WorkspaceId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationExportJobs_Status_LeaseExpiresAt_CreatedAt",
                table: "ConversationExportJobs",
                columns: new[] { "Status", "LeaseExpiresAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationExportJobs_WorkspaceId_CreatedAt",
                table: "ConversationExportJobs",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceEvents_EventId",
                table: "WorkspaceEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceEvents_WorkspaceId_Sequence",
                table: "WorkspaceEvents",
                columns: new[] { "WorkspaceId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigurationAuditEvents");

            migrationBuilder.DropTable(
                name: "ConversationExportJobs");

            migrationBuilder.DropTable(
                name: "WorkspaceEvents");

            migrationBuilder.DropTable(
                name: "WorkspaceEventWatermarks");
        }
    }
}
