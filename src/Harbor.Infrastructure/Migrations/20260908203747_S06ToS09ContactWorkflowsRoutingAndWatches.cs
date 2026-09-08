using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S06ToS09ContactWorkflowsRoutingAndWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoutingRuleId",
                table: "AssignmentEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoutingVersionId",
                table: "AssignmentEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContactImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CommittedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactImportBatches_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContactMergePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommittedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviewJson = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CommittedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactMergePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactMergePlans_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationWatchers",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationWatchers", x => new { x.ConversationId, x.TeammateId });
                    table.ForeignKey(
                        name: "FK_ConversationWatchers_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationWatchers_Teammates_TeammateId",
                        column: x => x.TeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InboxRoutingDefinitions",
                columns: table => new
                {
                    InboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActiveVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxRoutingDefinitions", x => x.InboxId);
                    table.ForeignKey(
                        name: "FK_InboxRoutingDefinitions_Inboxes_InboxId",
                        column: x => x.InboxId,
                        principalTable: "Inboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InboxRoutingVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RulesJson = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxRoutingVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxRoutingVersions_Inboxes_InboxId",
                        column: x => x.InboxId,
                        principalTable: "Inboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplyNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReadAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplyNotifications_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReplyNotifications_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReplyNotifications_Teammates_TeammateId",
                        column: x => x.TeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContactImportRows",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    NormalizedJson = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: true),
                    ErrorsJson = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactImportRows", x => new { x.BatchId, x.RowNumber });
                    table.ForeignKey(
                        name: "FK_ContactImportRows_ContactImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ContactImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactImportBatches_WorkspaceId_CreatedAt",
                table: "ContactImportBatches",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactMergePlans_WorkspaceId_CreatedAt",
                table: "ContactMergePlans",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationWatchers_TeammateId",
                table: "ConversationWatchers",
                column: "TeammateId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationWatchers_WorkspaceId_TeammateId",
                table: "ConversationWatchers",
                columns: new[] { "WorkspaceId", "TeammateId" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxRoutingVersions_InboxId_CreatedAt",
                table: "InboxRoutingVersions",
                columns: new[] { "InboxId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyNotifications_ConversationId",
                table: "ReplyNotifications",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyNotifications_MessageId_TeammateId",
                table: "ReplyNotifications",
                columns: new[] { "MessageId", "TeammateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplyNotifications_TeammateId",
                table: "ReplyNotifications",
                column: "TeammateId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyNotifications_WorkspaceId_TeammateId_CreatedAt",
                table: "ReplyNotifications",
                columns: new[] { "WorkspaceId", "TeammateId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactImportRows");

            migrationBuilder.DropTable(
                name: "ContactMergePlans");

            migrationBuilder.DropTable(
                name: "ConversationWatchers");

            migrationBuilder.DropTable(
                name: "InboxRoutingDefinitions");

            migrationBuilder.DropTable(
                name: "InboxRoutingVersions");

            migrationBuilder.DropTable(
                name: "ReplyNotifications");

            migrationBuilder.DropTable(
                name: "ContactImportBatches");

            migrationBuilder.DropColumn(
                name: "RoutingRuleId",
                table: "AssignmentEvents");

            migrationBuilder.DropColumn(
                name: "RoutingVersionId",
                table: "AssignmentEvents");
        }
    }
}
