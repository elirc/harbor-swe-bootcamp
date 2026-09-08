using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harbor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S01ToS05DurableIngressDeliveryAndCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ClaimGeneration",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LeaseExpiresAt",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "WebhookDeliveries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreationReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResponseStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RetainUntil = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreationReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreationReceipts_Teammates_TeammateId",
                        column: x => x.TeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreationReceipts_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InboundEmailReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedConversation = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedContact = table.Column<bool>(type: "INTEGER", nullable: false),
                    IngestedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundEmailReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboundEmailReceipts_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeammateApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeammateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeammateApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeammateApiKeys_Teammates_TeammateId",
                        column: x => x.TeammateId,
                        principalTable: "Teammates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeammateApiKeys_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    FinishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookAttempts_WebhookDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "WebhookDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreationReceipts_TeammateId",
                table: "CreationReceipts",
                column: "TeammateId");

            migrationBuilder.CreateIndex(
                name: "IX_CreationReceipts_WorkspaceId_TeammateId_Operation_Key",
                table: "CreationReceipts",
                columns: new[] { "WorkspaceId", "TeammateId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundEmailReceipts_WorkspaceId_InboxId_MessageId",
                table: "InboundEmailReceipts",
                columns: new[] { "WorkspaceId", "InboxId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeammateApiKeys_Hash",
                table: "TeammateApiKeys",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeammateApiKeys_TeammateId",
                table: "TeammateApiKeys",
                column: "TeammateId");

            migrationBuilder.CreateIndex(
                name: "IX_TeammateApiKeys_WorkspaceId_TeammateId",
                table: "TeammateApiKeys",
                columns: new[] { "WorkspaceId", "TeammateId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookAttempts_DeliveryId_AttemptNumber",
                table: "WebhookAttempts",
                columns: new[] { "DeliveryId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookAttempts_DeliveryId_ClaimGeneration",
                table: "WebhookAttempts",
                columns: new[] { "DeliveryId", "ClaimGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookAttempts_WorkspaceId_StartedAt",
                table: "WebhookAttempts",
                columns: new[] { "WorkspaceId", "StartedAt" });
            // Preserve every existing client's hash without requiring the raw key.
            // The teammate ID is a deterministic identity for its initial credential.
            migrationBuilder.Sql("""
                INSERT INTO TeammateApiKeys (Id, WorkspaceId, TeammateId, Name, Hash, CreatedAt, ExpiresAt, RevokedAt)
                SELECT Id, WorkspaceId, Id, 'Initial key', ApiKeyHash, CreatedAt, NULL, NULL
                FROM Teammates;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreationReceipts");

            migrationBuilder.DropTable(
                name: "InboundEmailReceipts");

            migrationBuilder.DropTable(
                name: "TeammateApiKeys");

            migrationBuilder.DropTable(
                name: "WebhookAttempts");

            migrationBuilder.DropColumn(
                name: "ClaimGeneration",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "WebhookDeliveries");
        }
    }
}
