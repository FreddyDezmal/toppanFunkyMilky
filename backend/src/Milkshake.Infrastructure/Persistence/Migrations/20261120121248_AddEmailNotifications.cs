using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_notifications",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notification_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    refund_id = table.Column<int>(type: "integer", nullable: true),
                    cancellation_request_id = table.Column<int>(type: "integer", nullable: true),
                    recipient_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_error = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    resend_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_notifications", x => x.id);
                    table.CheckConstraint("ck_email_notifications_counts", "attempt_count >= 0 AND resend_count >= 0 AND template_version >= 1");
                    table.CheckConstraint("ck_email_notifications_kind", "kind IN ('OrderConfirmed', 'CancellationRequested', 'OrderCancelled', 'RefundProcessed', 'RefundFailed')");
                    table.CheckConstraint("ck_email_notifications_last_error", "last_error IS NULL OR last_error IN ('Timeout', 'ProviderUnavailable', 'RateLimited', 'Rejected', 'InvalidRecipient')");
                    table.CheckConstraint("ck_email_notifications_lease", "(status = 'Sending') = (locked_until IS NOT NULL)");
                    table.CheckConstraint("ck_email_notifications_recipient", "recipient_email LIKE '%_@_%' AND recipient_email !~ '[[:cntrl:][:space:],<>]' AND recipient_name !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_email_notifications_refund", "(kind IN ('RefundProcessed', 'RefundFailed')) = (refund_id IS NOT NULL)");
                    table.CheckConstraint("ck_email_notifications_request", "(kind = 'CancellationRequested') = (cancellation_request_id IS NOT NULL)");
                    table.CheckConstraint("ck_email_notifications_sent", "(status = 'Sent') = (sent_at IS NOT NULL)");
                    table.CheckConstraint("ck_email_notifications_status", "status IN ('Pending', 'Sending', 'Sent', 'Failed', 'Suppressed')");
                    table.ForeignKey(
                        name: "fk_email_notifications_cancellation_requests_cancellation_requ",
                        column: x => x.cancellation_request_id,
                        principalSchema: "milkshake",
                        principalTable: "cancellation_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_email_notifications_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "milkshake",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_email_notifications_refunds_refund_id",
                        column: x => x.refund_id,
                        principalSchema: "milkshake",
                        principalTable: "refunds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_notifications_cancellation_request_id",
                schema: "milkshake",
                table: "email_notifications",
                column: "cancellation_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_notifications_notification_key",
                schema: "milkshake",
                table: "email_notifications",
                column: "notification_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_notifications_order_id",
                schema: "milkshake",
                table: "email_notifications",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_notifications_refund_id",
                schema: "milkshake",
                table: "email_notifications",
                column: "refund_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_notifications_status_next_attempt_at",
                schema: "milkshake",
                table: "email_notifications",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_notifications",
                schema: "milkshake");
        }
    }
}
