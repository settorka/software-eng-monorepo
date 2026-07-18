using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Settlement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSettlementSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OUTBOX_MESSAGES",
                columns: table => new
                {
                    OutboxMessageId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    MessageType = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "NCLOB", nullable: false),
                    PayloadHash = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    AttemptCount = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    LastError = table.Column<string>(type: "NVARCHAR2(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OUTBOX_MESSAGES", x => x.OutboxMessageId);
                });

            migrationBuilder.CreateTable(
                name: "TRADES",
                columns: table => new
                {
                    TradeId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    TradeVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    Commodity = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    Counterparty = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(24,8)", precision: 24, scale: 8, nullable: false),
                    Unit = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    Price = table.Column<decimal>(type: "DECIMAL(24,8)", precision: 24, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "NVARCHAR2(3)", maxLength: 3, nullable: false),
                    TradeDate = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    SettlementDate = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    PayloadHash = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRADES", x => new { x.TradeId, x.TradeVersion });
                });

            migrationBuilder.CreateTable(
                name: "SETTLEMENT_WORKFLOWS",
                columns: table => new
                {
                    WorkflowId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    TradeId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    TradeVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    State = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    WorkflowVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SETTLEMENT_WORKFLOWS", x => x.WorkflowId);
                    table.ForeignKey(
                        name: "FK_SETTLEMENT_WORKFLOWS_TRADES_TradeId_TradeVersion",
                        columns: x => new { x.TradeId, x.TradeVersion },
                        principalTable: "TRADES",
                        principalColumns: new[] { "TradeId", "TradeVersion" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AUDIT_EVENTS",
                columns: table => new
                {
                    AuditEventId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    TradeId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    TradeVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    EventType = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    Details = table.Column<string>(type: "NVARCHAR2(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AUDIT_EVENTS", x => x.AuditEventId);
                    table.ForeignKey(
                        name: "FK_AUDIT_EVENTS_SETTLEMENT_WORKFLOWS_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "SETTLEMENT_WORKFLOWS",
                        principalColumn: "WorkflowId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SETTLEMENTS",
                columns: table => new
                {
                    SettlementId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    TradeId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    TradeVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(24,8)", precision: 24, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "NVARCHAR2(3)", maxLength: 3, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SETTLEMENTS", x => x.SettlementId);
                    table.ForeignKey(
                        name: "FK_SETTLEMENTS_SETTLEMENT_WORKFLOWS_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "SETTLEMENT_WORKFLOWS",
                        principalColumn: "WorkflowId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WORKFLOW_TRANSITIONS",
                columns: table => new
                {
                    WorkflowTransitionId = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    WorkflowId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Sequence = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    FromState = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    ToState = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "NVARCHAR2(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WORKFLOW_TRANSITIONS", x => x.WorkflowTransitionId);
                    table.ForeignKey(
                        name: "FK_WORKFLOW_TRANSITIONS_SETTLEMENT_WORKFLOWS_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "SETTLEMENT_WORKFLOWS",
                        principalColumn: "WorkflowId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "INVOICES",
                columns: table => new
                {
                    InvoiceId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    SettlementId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVOICES", x => x.InvoiceId);
                    table.ForeignKey(
                        name: "FK_INVOICES_SETTLEMENTS_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "SETTLEMENTS",
                        principalColumn: "SettlementId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT_REQUESTS",
                columns: table => new
                {
                    PaymentRequestId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_REQUESTS", x => x.PaymentRequestId);
                    table.ForeignKey(
                        name: "FK_PAYMENT_REQUESTS_INVOICES_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "INVOICES",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AUDIT_EVENTS_TradeId_TradeVersion_OccurredAt",
                table: "AUDIT_EVENTS",
                columns: new[] { "TradeId", "TradeVersion", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AUDIT_EVENTS_WorkflowId",
                table: "AUDIT_EVENTS",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICES_InvoiceNumber",
                table: "INVOICES",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_INVOICES_SettlementId",
                table: "INVOICES",
                column: "SettlementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OUTBOX_MESSAGES_MessageType_PayloadHash",
                table: "OUTBOX_MESSAGES",
                columns: new[] { "MessageType", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OUTBOX_MESSAGES_Status_NextAttemptAt",
                table: "OUTBOX_MESSAGES",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_REQUESTS_IdempotencyKey",
                table: "PAYMENT_REQUESTS",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_REQUESTS_InvoiceId",
                table: "PAYMENT_REQUESTS",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SETTLEMENT_WORKFLOWS_TradeId_TradeVersion",
                table: "SETTLEMENT_WORKFLOWS",
                columns: new[] { "TradeId", "TradeVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SETTLEMENTS_WorkflowId",
                table: "SETTLEMENTS",
                column: "WorkflowId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WORKFLOW_TRANSITIONS_WorkflowId_Sequence",
                table: "WORKFLOW_TRANSITIONS",
                columns: new[] { "WorkflowId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AUDIT_EVENTS");

            migrationBuilder.DropTable(
                name: "OUTBOX_MESSAGES");

            migrationBuilder.DropTable(
                name: "PAYMENT_REQUESTS");

            migrationBuilder.DropTable(
                name: "WORKFLOW_TRANSITIONS");

            migrationBuilder.DropTable(
                name: "INVOICES");

            migrationBuilder.DropTable(
                name: "SETTLEMENTS");

            migrationBuilder.DropTable(
                name: "SETTLEMENT_WORKFLOWS");

            migrationBuilder.DropTable(
                name: "TRADES");
        }
    }
}
