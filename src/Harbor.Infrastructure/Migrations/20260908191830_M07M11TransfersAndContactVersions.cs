using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M07M11TransfersAndContactVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Contacts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("7e665f7d-f82d-4a96-b10b-96b74b4dc102"));

            migrationBuilder.CreateTable(
                name: "ConversationTransferEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorTeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromInboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToInboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTransferEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationTransferEvents_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTransferEvents_ConversationId_CreatedAt_Id",
                table: "ConversationTransferEvents",
                columns: new[] { "ConversationId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationTransferEvents");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Contacts");
        }
    }
}
