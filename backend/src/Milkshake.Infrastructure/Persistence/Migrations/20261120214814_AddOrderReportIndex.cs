using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_orders_created_at_id",
                schema: "milkshake",
                table: "orders",
                columns: new[] { "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_created_at_id",
                schema: "milkshake",
                table: "orders");
        }
    }
}
