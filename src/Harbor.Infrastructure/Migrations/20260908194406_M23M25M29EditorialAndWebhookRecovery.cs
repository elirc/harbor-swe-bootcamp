using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M23M25M29EditorialAndWebhookRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OriginalDeliveryId",
                table: "WebhookDeliveries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Articles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("8bb6174c-b78a-448f-b65f-bad2a07e7919"));

            migrationBuilder.CreateTable(
                name: "ArticleFeedback",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Helpful = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleFeedback", x => new { x.ArticleId, x.TeammateId });
                    table.ForeignKey(
                        name: "FK_ArticleFeedback_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleFeedback_Teammates_TeammateId",
                        column: x => x.TeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleFeedback_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArticleRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArticleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceVersion = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArticleRevisions_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleRevisions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_OriginalDeliveryId",
                table: "WebhookDeliveries",
                column: "OriginalDeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleFeedback_TeammateId",
                table: "ArticleFeedback",
                column: "TeammateId");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleFeedback_WorkspaceId_TeammateId",
                table: "ArticleFeedback",
                columns: new[] { "WorkspaceId", "TeammateId" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleRevisions_ArticleId_RecordedAt_Id",
                table: "ArticleRevisions",
                columns: new[] { "ArticleId", "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleRevisions_WorkspaceId",
                table: "ArticleRevisions",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleFeedback");

            migrationBuilder.DropTable(
                name: "ArticleRevisions");

            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_OriginalDeliveryId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "OriginalDeliveryId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Articles");
        }
    }
}
