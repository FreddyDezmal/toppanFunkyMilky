using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_events",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character(16)", fixedLength: true, maxLength: 16, nullable: false),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    refund_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    provider_transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    checkout_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    provider_message = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_positive", "amount_minor > 0");
                    table.CheckConstraint("ck_payments_checkout_url", "checkout_url IS NULL OR checkout_url LIKE 'https://%'");
                    table.CheckConstraint("ck_payments_currency", "currency_code ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_payments_reference", "reference ~ '^MSP-[0-9A-HJKMNP-TV-Z]{12}$'");
                    table.CheckConstraint("ck_payments_refund_reason", "(status = 'RequiresRefund' AND refund_reason IS NOT NULL AND refund_reason IN ('OrderNotPayable', 'AlreadyPaid', 'AmountMismatch')) OR (status <> 'RequiresRefund' AND refund_reason IS NULL)");
                    table.CheckConstraint("ck_payments_status", "status IN ('Initiating', 'Pending', 'Succeeded', 'Failed', 'Abandoned', 'Superseded', 'RequiresRefund')");
                    table.ForeignKey(
                        name: "fk_payments_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "milkshake",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_events_provider_event_type_provider_transaction_id",
                schema: "milkshake",
                table: "payment_events",
                columns: new[] { "provider", "event_type", "provider_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_events_reference",
                schema: "milkshake",
                table: "payment_events",
                column: "reference");

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id_idempotency_key",
                schema: "milkshake",
                table: "payments",
                columns: new[] { "order_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_provider_provider_transaction_id",
                schema: "milkshake",
                table: "payments",
                columns: new[] { "provider", "provider_transaction_id" },
                unique: true,
                filter: "provider_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_reference",
                schema: "milkshake",
                table: "payments",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_status_created_at",
                schema: "milkshake",
                table: "payments",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_one_open_per_order",
                schema: "milkshake",
                table: "payments",
                column: "order_id",
                unique: true,
                filter: "status IN ('Initiating', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "ux_payments_one_success_per_order",
                schema: "milkshake",
                table: "payments",
                column: "order_id",
                unique: true,
                filter: "status = 'Succeeded'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_events",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "milkshake");
        }
    }
}
