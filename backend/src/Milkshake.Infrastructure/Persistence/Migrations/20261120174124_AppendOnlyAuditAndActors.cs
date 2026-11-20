using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Milkshake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppendOnlyAuditAndActors : Migration
    {
        private static readonly string[] AppendOnlyTables = ["audit_logs", "security_events"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor",
                schema: "milkshake",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_security_events_occurred_at_id",
                schema: "milkshake",
                table: "security_events",
                columns: new[] { "occurred_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_occurred_at_id",
                schema: "milkshake",
                table: "audit_logs",
                columns: new[] { "occurred_at", "id" },
                descending: new bool[0]);

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_actor",
                schema: "milkshake",
                table: "audit_logs",
                sql: "actor IS NULL OR actor ~ '^(user|anonymous|system:[a-z][a-z-]{0,40})$'");

            // AC-11.3 (M11): audit history is append-only. Any UPDATE, DELETE or TRUNCATE of audit_logs or
            // security_events is refused, whoever runs it through the application's connection. Only the database
            // owner could disable these triggers (outside the application; documented).
            migrationBuilder.Sql("""
                CREATE FUNCTION milkshake.refuse_audit_change() RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = ''
                AS $$
                BEGIN
                    RAISE EXCEPTION 'milkshake.% is append-only: audit history cannot be changed or deleted', TG_TABLE_NAME;
                END;
                $$;
                """);

            foreach (string table in AppendOnlyTables)
            {
                migrationBuilder.Sql(
                    $"CREATE TRIGGER trg_{table}_append_only BEFORE UPDATE OR DELETE ON milkshake.{table} " +
                    "FOR EACH ROW EXECUTE FUNCTION milkshake.refuse_audit_change();");
                migrationBuilder.Sql(
                    $"CREATE TRIGGER trg_{table}_no_truncate BEFORE TRUNCATE ON milkshake.{table} " +
                    "FOR EACH STATEMENT EXECUTE FUNCTION milkshake.refuse_audit_change();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string table in AppendOnlyTables)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{table}_no_truncate ON milkshake.{table};");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{table}_append_only ON milkshake.{table};");
            }

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS milkshake.refuse_audit_change();");

            migrationBuilder.DropIndex(
                name: "ix_security_events_occurred_at_id",
                schema: "milkshake",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_occurred_at_id",
                schema: "milkshake",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_actor",
                schema: "milkshake",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "actor",
                schema: "milkshake",
                table: "audit_logs");
        }
    }
}
