using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellationsAndRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cancellation_requests",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cancellation_requests", x => x.id);
                    table.CheckConstraint("ck_cancellation_requests_closed", "(status = 'Pending') = (closed_at IS NULL)");
                    table.CheckConstraint("ck_cancellation_requests_reason", "char_length(reason) BETWEEN 1 AND 50 AND reason = btrim(reason) AND reason !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_cancellation_requests_status", "status IN ('Pending', 'Accepted', 'Declined', 'Lapsed')");
                    table.ForeignKey(
                        name: "fk_cancellation_requests_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "milkshake",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cancellation_requests_users_requested_by",
                        column: x => x.requested_by,
                        principalSchema: "milkshake",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character(16)", fixedLength: true, maxLength: 16, nullable: false),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    payment_id = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    paid_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    fee_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_refund_id = table.Column<long>(type: "bigint", nullable: true),
                    provider_message = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amounts", "paid_amount_minor > 0 AND fee_minor >= 0 AND amount_minor > 0 AND amount_minor = paid_amount_minor - fee_minor");
                    table.CheckConstraint("ck_refunds_completed", "(status IN ('Processed', 'Failed')) = (completed_at IS NOT NULL)");
                    table.CheckConstraint("ck_refunds_currency", "currency_code ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_refunds_fee_rate", "fee_rate >= 0 AND fee_rate <= 0.5 AND (kind = 'PatronCancellation' OR (fee_rate = 0 AND fee_minor = 0))");
                    table.CheckConstraint("ck_refunds_kind", "kind IN ('PatronCancellation', 'RestaurantCancellation', 'UnacceptedPayment')");
                    table.CheckConstraint("ck_refunds_provider_id", "status <> 'Pending' OR provider_refund_id IS NOT NULL");
                    table.CheckConstraint("ck_refunds_reference", "reference ~ '^MSR-[0-9A-HJKMNP-TV-Z]{12}$'");
                    table.CheckConstraint("ck_refunds_status", "status IN ('Initiating', 'Pending', 'Processed', 'Failed')");
                    table.ForeignKey(
                        name: "fk_refunds_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "milkshake",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refunds_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "milkshake",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cancellation_requests_order_id",
                schema: "milkshake",
                table: "cancellation_requests",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_cancellation_requests_requested_by",
                schema: "milkshake",
                table: "cancellation_requests",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_cancellation_requests_status",
                schema: "milkshake",
                table: "cancellation_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_cancellation_requests_one_pending_per_order",
                schema: "milkshake",
                table: "cancellation_requests",
                column: "order_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_order_id",
                schema: "milkshake",
                table: "refunds",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_provider_provider_refund_id",
                schema: "milkshake",
                table: "refunds",
                columns: new[] { "provider", "provider_refund_id" },
                unique: true,
                filter: "provider_refund_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_reference",
                schema: "milkshake",
                table: "refunds",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refunds_status_created_at",
                schema: "milkshake",
                table: "refunds",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_refunds_one_per_payment",
                schema: "milkshake",
                table: "refunds",
                column: "payment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cancellation_requests",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "milkshake");
        }
    }
}
