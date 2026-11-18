using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaseInsensitiveNamesAndRowVersions : Migration
    {
        // Tables whose names must be unique ignoring letter case (M10 design decision D2).
        private static readonly string[] CaseInsensitiveNameTables = ["flavours", "toppings", "consistencies", "restaurants"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_toppings_name",
                schema: "milkshake",
                table: "toppings");

            migrationBuilder.DropIndex(
                name: "ix_restaurants_name",
                schema: "milkshake",
                table: "restaurants");

            migrationBuilder.DropIndex(
                name: "ix_flavours_name",
                schema: "milkshake",
                table: "flavours");

            migrationBuilder.DropIndex(
                name: "ix_consistencies_name",
                schema: "milkshake",
                table: "consistencies");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "milkshake",
                table: "toppings",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "milkshake",
                table: "system_settings",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "milkshake",
                table: "restaurants",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "milkshake",
                table: "flavours",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "milkshake",
                table: "discount_tiers",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "milkshake",
                table: "consistencies",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // Fails, and the whole migration rolls back, if existing rows already differ only by letter case.
            foreach (string table in CaseInsensitiveNameTables)
            {
                migrationBuilder.Sql($"CREATE UNIQUE INDEX ux_{table}_lower_name ON milkshake.{table} (lower(name));");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
                        foreach (string table in CaseInsensitiveNameTables)
            {
                migrationBuilder.Sql($"DROP INDEX IF EXISTS milkshake.ux_{table}_lower_name;");
            }
            
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "milkshake",
                table: "toppings");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "milkshake",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "milkshake",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "milkshake",
                table: "flavours");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "milkshake",
                table: "discount_tiers");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "milkshake",
                table: "consistencies");

            migrationBuilder.CreateIndex(
                name: "ix_toppings_name",
                schema: "milkshake",
                table: "toppings",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_restaurants_name",
                schema: "milkshake",
                table: "restaurants",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flavours_name",
                schema: "milkshake",
                table: "flavours",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consistencies_name",
                schema: "milkshake",
                table: "consistencies",
                column: "name",
                unique: true);
        }
    }
}
