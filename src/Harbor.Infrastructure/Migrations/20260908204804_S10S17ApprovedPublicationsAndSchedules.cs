using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S10S17ApprovedPublicationsAndSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArticlePublicationSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArticleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduledByTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpectedArticleVersion = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticlePublicationSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArticlePublicationSchedules_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArticlePublicationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArticleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedByTeammateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstPublishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ArticleCreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticlePublicationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArticlePublicationSnapshots_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActiveArticlePublications",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveArticlePublications", x => x.ArticleId);
                    table.ForeignKey(
                        name: "FK_ActiveArticlePublications_ArticleCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "ArticleCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActiveArticlePublications_ArticlePublicationSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "ArticlePublicationSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActiveArticlePublications_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveArticlePublications_CollectionId",
                table: "ActiveArticlePublications",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveArticlePublications_SnapshotId",
                table: "ActiveArticlePublications",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveArticlePublications_WorkspaceId_Slug",
                table: "ActiveArticlePublications",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticlePublicationSchedules_ArticleId",
                table: "ArticlePublicationSchedules",
                column: "ArticleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticlePublicationSchedules_Status_PublishAt",
                table: "ArticlePublicationSchedules",
                columns: new[] { "Status", "PublishAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticlePublicationSnapshots_ArticleId_PublishedAt",
                table: "ArticlePublicationSnapshots",
                columns: new[] { "ArticleId", "PublishedAt" });

            // Existing public content becomes an approved snapshot; drafts remain private.
            // The earlier SQLite column backfill used one default token. Bind legacy
            // initial article versions to their own unique IDs before scheduling uses them.
            migrationBuilder.Sql("UPDATE Articles SET Version = Id WHERE upper(Version) = '8BB6174C-B78A-448F-B65F-BAD2A07E7919';");
            migrationBuilder.Sql("INSERT INTO ArticlePublicationSnapshots (Id,ArticleId,WorkspaceId,CollectionId,PublishedByTeammateId,Title,Slug,Body,PublishedAt,FirstPublishedAt,ArticleCreatedAt,UpdatedAt) SELECT Id,Id,WorkspaceId,CollectionId,NULL,Title,Slug,Body,COALESCE(PublishedAt,UpdatedAt),PublishedAt,CreatedAt,UpdatedAt FROM Articles WHERE Status = 1;");
            migrationBuilder.Sql("INSERT INTO ActiveArticlePublications (ArticleId,WorkspaceId,CollectionId,SnapshotId,Slug) SELECT Id,WorkspaceId,CollectionId,Id,Slug FROM Articles WHERE Status = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveArticlePublications");

            migrationBuilder.DropTable(
                name: "ArticlePublicationSchedules");

            migrationBuilder.DropTable(
                name: "ArticlePublicationSnapshots");
        }
    }
}
