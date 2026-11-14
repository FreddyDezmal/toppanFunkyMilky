using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "milkshake");

            migrationBuilder.CreateSequence(
                name: "EntityFrameworkHiLoSequence",
                schema: "milkshake",
                incrementBy: 10);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    table_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consistencies",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consistencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discount_tiers",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    tier_number = table.Column<int>(type: "integer", nullable: false),
                    min_previous_orders = table.Column<int>(type: "integer", nullable: false),
                    min_drinks_per_order = table.Column<int>(type: "integer", nullable: false),
                    discount_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_tiers", x => x.id);
                    table.CheckConstraint("ck_discount_tiers_discount_rate", "discount_rate >= 0 AND discount_rate <= 1");
                    table.CheckConstraint("ck_discount_tiers_thresholds", "min_previous_orders >= 0 AND min_drinks_per_order >= 1");
                    table.CheckConstraint("ck_discount_tiers_tier_number", "tier_number BETWEEN 1 AND 3");
                });

            migrationBuilder.CreateTable(
                name: "flavours",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flavours", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "restaurants",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                schema: "milkshake",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    value_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "toppings",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_toppings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consistency_prices",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    consistency_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consistency_prices", x => x.id);
                    table.CheckConstraint("ck_consistency_prices_amount_non_negative", "amount >= 0");
                    table.CheckConstraint("ck_consistency_prices_effective_range", "effective_to IS NULL OR effective_to > effective_from");
                    table.ForeignKey(
                        name: "fk_consistency_prices_consistencies_consistency_id",
                        column: x => x.consistency_id,
                        principalSchema: "milkshake",
                        principalTable: "consistencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flavour_prices",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    flavour_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flavour_prices", x => x.id);
                    table.CheckConstraint("ck_flavour_prices_amount_non_negative", "amount >= 0");
                    table.CheckConstraint("ck_flavour_prices_effective_range", "effective_to IS NULL OR effective_to > effective_from");
                    table.ForeignKey(
                        name: "fk_flavour_prices_flavours_flavour_id",
                        column: x => x.flavour_id,
                        principalSchema: "milkshake",
                        principalTable: "flavours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "milkshake",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "topping_prices",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    topping_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_topping_prices", x => x.id);
                    table.CheckConstraint("ck_topping_prices_amount_non_negative", "amount >= 0");
                    table.CheckConstraint("ck_topping_prices_effective_range", "effective_to IS NULL OR effective_to > effective_from");
                    table.ForeignKey(
                        name: "fk_topping_prices_toppings_topping_id",
                        column: x => x.topping_id,
                        principalSchema: "milkshake",
                        principalTable: "toppings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "milkshake",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "milkshake",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "milkshake",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "milkshake",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "milkshake",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "milkshake",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "milkshake",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "milkshake",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "milkshake",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_occurred_at",
                schema: "milkshake",
                table: "audit_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_table_name_record_id",
                schema: "milkshake",
                table: "audit_logs",
                columns: new[] { "table_name", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                schema: "milkshake",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_consistencies_name",
                schema: "milkshake",
                table: "consistencies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consistency_prices_consistency_id",
                schema: "milkshake",
                table: "consistency_prices",
                column: "consistency_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_consistency_prices_consistency_id_effective_from",
                schema: "milkshake",
                table: "consistency_prices",
                columns: new[] { "consistency_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discount_tiers_tier_number",
                schema: "milkshake",
                table: "discount_tiers",
                column: "tier_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flavour_prices_flavour_id",
                schema: "milkshake",
                table: "flavour_prices",
                column: "flavour_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_flavour_prices_flavour_id_effective_from",
                schema: "milkshake",
                table: "flavour_prices",
                columns: new[] { "flavour_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flavours_name",
                schema: "milkshake",
                table: "flavours",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_restaurants_name",
                schema: "milkshake",
                table: "restaurants",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                schema: "milkshake",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "milkshake",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_topping_prices_topping_id",
                schema: "milkshake",
                table: "topping_prices",
                column: "topping_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_topping_prices_topping_id_effective_from",
                schema: "milkshake",
                table: "topping_prices",
                columns: new[] { "topping_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_toppings_name",
                schema: "milkshake",
                table: "toppings",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "milkshake",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "milkshake",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "milkshake",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "milkshake",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "milkshake",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "consistency_prices",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "discount_tiers",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "flavour_prices",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "restaurants",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "system_settings",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "topping_prices",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "consistencies",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "flavours",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "toppings",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "milkshake");

            migrationBuilder.DropTable(
                name: "users",
                schema: "milkshake");

            migrationBuilder.DropSequence(
                name: "EntityFrameworkHiLoSequence",
                schema: "milkshake");
        }
    }
}
