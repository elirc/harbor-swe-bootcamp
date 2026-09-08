using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M01M02M03PersonalWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationBookmarks",
                columns: table => new
                {
                    TeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationBookmarks", x => new { x.TeammateId, x.ConversationId });
                    table.ForeignKey(
                        name: "FK_ConversationBookmarks_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationBookmarks_Teammates_TeammateId",
                        column: x => x.TeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationBookmarks_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplyDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplyDrafts_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReplyDrafts_Teammates_OwnerTeammateId",
                        column: x => x.OwnerTeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReplyDrafts_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedQueues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FilterJson = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedQueues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedQueues_Teammates_OwnerTeammateId",
                        column: x => x.OwnerTeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedQueues_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationBookmarks_ConversationId",
                table: "ConversationBookmarks",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationBookmarks_WorkspaceId_TeammateId_CreatedAt",
                table: "ConversationBookmarks",
                columns: new[] { "WorkspaceId", "TeammateId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_ConversationId_OwnerTeammateId",
                table: "ReplyDrafts",
                columns: new[] { "ConversationId", "OwnerTeammateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_OwnerTeammateId",
                table: "ReplyDrafts",
                column: "OwnerTeammateId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_WorkspaceId_OwnerTeammateId",
                table: "ReplyDrafts",
                columns: new[] { "WorkspaceId", "OwnerTeammateId" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedQueues_OwnerTeammateId",
                table: "SavedQueues",
                column: "OwnerTeammateId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedQueues_WorkspaceId_OwnerTeammateId",
                table: "SavedQueues",
                columns: new[] { "WorkspaceId", "OwnerTeammateId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationBookmarks");

            migrationBuilder.DropTable(
                name: "ReplyDrafts");

            migrationBuilder.DropTable(
                name: "SavedQueues");
        }
    }
}
