using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nodo.EventLifecycle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "event_lifecycle");

            migrationBuilder.CreateTable(
                name: "customer_credits",
                schema: "event_lifecycle",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    balance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_credits", x => x.customer_id);
                    table.CheckConstraint("ck_customer_credits_inv21_balance_non_negative", "balance_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "hall_events",
                schema: "event_lifecycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hall_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    attendance = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    deposit_percentage = table.Column<int>(type: "integer", nullable: true),
                    deposit_required_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    deposit_required_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    deposit_accredited_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    deposit_accredited_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    deposit_transaction_ids = table.Column<string[]>(type: "text[]", nullable: true),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decision_deadline_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deposit_deadline_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_approved = table.Column<bool>(type: "boolean", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    credit_generated_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    credit_generated_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reschedule_count = table.Column<int>(type: "integer", nullable: false),
                    extra_services_assigned = table.Column<bool>(type: "boolean", nullable: false),
                    remaining_to_settle_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    remaining_to_settle_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hall_events", x => x.id);
                    table.CheckConstraint("ck_hall_events_inv01_duration", "end_time > start_time AND end_time - start_time BETWEEN interval '1 hour' AND interval '8 hours'");
                    table.CheckConstraint("ck_hall_events_inv02_deposit_percentage", "deposit_percentage IS NULL OR deposit_percentage BETWEEN 20 AND 50");
                    table.CheckConstraint("ck_hall_events_inv04_attendance", "attendance BETWEEN 1 AND 80");
                    table.CheckConstraint("ck_hall_events_inv05_credit_generated", "credit_generated_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "event_lifecycle",
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
                schema: "event_lifecycle",
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
                name: "customer_credit_movements",
                schema: "event_lifecycle",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_credit_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_credit_movements_customer_credits_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "event_lifecycle",
                        principalTable: "customer_credits",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "extra_service_items",
                schema: "event_lifecycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit_cost_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    hall_event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extra_service_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_extra_service_items_hall_events_hall_event_id",
                        column: x => x.hall_event_id,
                        principalSchema: "event_lifecycle",
                        principalTable: "hall_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hall_event_history",
                schema: "event_lifecycle",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    hall_event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hall_event_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_hall_event_history_hall_events_hall_event_id",
                        column: x => x.hall_event_id,
                        principalSchema: "event_lifecycle",
                        principalTable: "hall_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_credit_movements_customer_id",
                schema: "event_lifecycle",
                table: "customer_credit_movements",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_extra_service_items_hall_event_id",
                schema: "event_lifecycle",
                table: "extra_service_items",
                column: "hall_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_hall_event_history_hall_event_id",
                schema: "event_lifecycle",
                table: "hall_event_history",
                column: "hall_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_hall_events_customer_id",
                schema: "event_lifecycle",
                table: "hall_events",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_hall_events_hall_id_status",
                schema: "event_lifecycle",
                table: "hall_events",
                columns: new[] { "hall_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hall_events_status_decision_deadline_utc",
                schema: "event_lifecycle",
                table: "hall_events",
                columns: new[] { "status", "decision_deadline_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "event_lifecycle",
                table: "outbox_messages",
                column: "occurred_on_utc",
                filter: "processed_on_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_credit_movements",
                schema: "event_lifecycle");

            migrationBuilder.DropTable(
                name: "extra_service_items",
                schema: "event_lifecycle");

            migrationBuilder.DropTable(
                name: "hall_event_history",
                schema: "event_lifecycle");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "event_lifecycle");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "event_lifecycle");

            migrationBuilder.DropTable(
                name: "customer_credits",
                schema: "event_lifecycle");

            migrationBuilder.DropTable(
                name: "hall_events",
                schema: "event_lifecycle");
        }
    }
}
