using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: false),
                    patron_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    restaurant_id = table.Column<int>(type: "integer", nullable: false),
                    restaurant_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    restaurant_address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    pickup_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pickup_time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_tier_number = table.Column<int>(type: "integer", nullable: true),
                    discount_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    discount_cap_applied = table.Column<bool>(type: "boolean", nullable: false),
                    taxable = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    priced_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_amounts_non_negative", "subtotal >= 0 AND discount_amount >= 0 AND taxable >= 0 AND vat >= 0 AND total >= 0");
                    table.CheckConstraint("ck_orders_discount_rate", "(discount_tier_number IS NULL AND discount_rate IS NULL) OR (discount_tier_number >= 1 AND discount_rate >= 0 AND discount_rate <= 1)");
                    table.CheckConstraint("ck_orders_discount_within_subtotal", "discount_amount <= subtotal");
                    table.CheckConstraint("ck_orders_reference", "reference ~ '^MS-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$'");
                    table.CheckConstraint("ck_orders_request_fingerprint", "request_fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_orders_status", "status IN ('PendingPayment', 'Confirmed', 'PaymentFailed', 'Cancelled')");
                    table.CheckConstraint("ck_orders_taxable", "taxable = subtotal - discount_amount");
                    table.CheckConstraint("ck_orders_total", "total = taxable + vat");
                    table.CheckConstraint("ck_orders_vat_rate", "vat_rate >= 0 AND vat_rate <= 1");
                    table.ForeignKey(
                        name: "fk_orders_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalSchema: "milkshake",
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_users_patron_id",
                        column: x => x.patron_id,
                        principalSchema: "milkshake",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    flavour_id = table.Column<int>(type: "integer", nullable: false),
                    flavour_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    flavour_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    consistency_id = table.Column<int>(type: "integer", nullable: false),
                    consistency_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    consistency_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    topping_id = table.Column<int>(type: "integer", nullable: false),
                    topping_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    topping_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.CheckConstraint("ck_order_items_fees_non_negative", "flavour_fee >= 0 AND consistency_fee >= 0 AND topping_fee >= 0");
                    table.CheckConstraint("ck_order_items_number", "number >= 1");
                    table.CheckConstraint("ck_order_items_price", "price = flavour_fee + consistency_fee + topping_fee");
                    table.ForeignKey(
                        name: "fk_order_items_consistencies_consistency_id",
                        column: x => x.consistency_id,
                        principalSchema: "milkshake",
                        principalTable: "consistencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_flavours_flavour_id",
                        column: x => x.flavour_id,
                        principalSchema: "milkshake",
                        principalTable: "flavours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "milkshake",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_toppings_topping_id",
                        column: x => x.topping_id,
                        principalSchema: "milkshake",
                        principalTable: "toppings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_consistency_id",
                schema: "milkshake",
                table: "order_items",
                column: "consistency_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_flavour_id",
                schema: "milkshake",
                table: "order_items",
                column: "flavour_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id_number",
                schema: "milkshake",
                table: "order_items",
                columns: new[] { "order_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_items_topping_id",
                schema: "milkshake",
                table: "order_items",
                column: "topping_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_patron_id_created_at",
                schema: "milkshake",
                table: "orders",
                columns: new[] { "patron_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_patron_id_idempotency_key",
                schema: "milkshake",
                table: "orders",
                columns: new[] { "patron_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_patron_id_status",
                schema: "milkshake",
                table: "orders",
                columns: new[] { "patron_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_reference",
                schema: "milkshake",
                table: "orders",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_restaurant_id",
                schema: "milkshake",
                table: "orders",
                column: "restaurant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "milkshake");
        }
    }
}
