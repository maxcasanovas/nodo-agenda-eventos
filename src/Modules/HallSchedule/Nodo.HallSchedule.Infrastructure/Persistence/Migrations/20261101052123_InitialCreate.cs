using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nodo.HallSchedule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Needed to combine "hall_id WITH =" with a range "WITH &&" in one exclusion constraint.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.EnsureSchema(
                name: "hall_schedule");

            migrationBuilder.CreateTable(
                name: "hall_schedules",
                schema: "hall_schedule",
                columns: table => new
                {
                    hall_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hall_schedules", x => new { x.hall_id, x.date });
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "hall_schedule",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "hall_schedule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_blocks",
                schema: "hall_schedule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    buffered_end = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, computedColumnSql: "(\"date\" + end_time) + interval '1 hour'", stored: true),
                    buffered_start = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, computedColumnSql: "(\"date\" + start_time) - interval '1 hour'", stored: true),
                    hall_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_blocks", x => x.id);
                    table.CheckConstraint("ck_schedule_blocks_inv01_duration", "end_time > start_time AND end_time - start_time BETWEEN interval '1 hour' AND interval '8 hours'");
                    table.CheckConstraint("ck_schedule_blocks_same_day", "\"date\" = schedule_date");
                    table.ForeignKey(
                        name: "fk_schedule_blocks_hall_schedules_hall_id_schedule_date",
                        columns: x => new { x.hall_id, x.schedule_date },
                        principalSchema: "hall_schedule",
                        principalTable: "hall_schedules",
                        principalColumns: new[] { "hall_id", "date" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_block_equipment",
                schema: "hall_schedule",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    block_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_block_equipment", x => x.id);
                    table.ForeignKey(
                        name: "fk_schedule_block_equipment_schedule_blocks_block_id",
                        column: x => x.block_id,
                        principalSchema: "hall_schedule",
                        principalTable: "schedule_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "hall_schedule",
                table: "outbox_messages",
                column: "occurred_on_utc",
                filter: "processed_on_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_block_equipment_block_id",
                schema: "hall_schedule",
                table: "schedule_block_equipment",
                column: "block_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_blocks_hall_id_schedule_date",
                schema: "hall_schedule",
                table: "schedule_blocks",
                columns: new[] { "hall_id", "schedule_date" });

            migrationBuilder.CreateIndex(
                name: "ux_schedule_blocks_inv19_one_active_block_per_event",
                schema: "hall_schedule",
                table: "schedule_blocks",
                column: "event_id",
                unique: true,
                filter: "status = 'Active'");

            // INV-18 (BR-03): no two active blocks of the same hall may overlap, buffers included. The domain checks this
            // first (422); this constraint is the defense in depth that also covers concurrent requests (409).
            // buffered_start / buffered_end are generated columns: day + time -/+ 1 h.
            migrationBuilder.Sql("""
                ALTER TABLE hall_schedule.schedule_blocks
                    ADD CONSTRAINT ex_schedule_blocks_inv18_overlap
                    EXCLUDE USING gist (
                        hall_id WITH =,
                        tsrange(buffered_start, buffered_end, '[)') WITH &&)
                    WHERE (status = 'Active');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "hall_schedule");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "hall_schedule");

            migrationBuilder.DropTable(
                name: "schedule_block_equipment",
                schema: "hall_schedule");

            migrationBuilder.DropTable(
                name: "schedule_blocks",
                schema: "hall_schedule");

            migrationBuilder.DropTable(
                name: "hall_schedules",
                schema: "hall_schedule");
        }
    }
}
