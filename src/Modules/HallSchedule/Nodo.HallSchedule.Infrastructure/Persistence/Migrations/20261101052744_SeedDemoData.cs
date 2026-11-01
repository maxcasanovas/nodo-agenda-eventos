using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodo.HallSchedule.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Demo schedule (business-rules.md §8). D is today in La Paz, so the dates match what the application calls "today".
    /// Blocks are coherent with the events of the EventLifecycle seed:
    ///   E1 (D+10 14-18) provisional, E2 (D+14 09-15) provisional,
    ///   E3 (D+7 10-14, projector) definitive, E4 (D-5 08-16) definitive,
    ///   E5 (D+20 10-15, lights) released, E6 (D+9 14-17) released.
    /// Fixed ids: block 66666666-0000-0000-0000-00000000000N belongs to event 44444444-0000-0000-0000-00000000000N.
    /// </summary>
    public partial class SeedDemoData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    d date := (now() AT TIME ZONE 'America/La_Paz')::date;
                    n timestamptz := now();
                    hall uuid := '11111111-1111-1111-1111-111111111111';
                BEGIN
                    INSERT INTO hall_schedule.hall_schedules (hall_id, date) VALUES
                        (hall, d + 10), (hall, d + 14), (hall, d + 7), (hall, d - 5), (hall, d + 20), (hall, d + 9);

                    INSERT INTO hall_schedule.schedule_blocks
                        (id, event_id, hall_id, schedule_date, date, start_time, end_time, kind, status, created_at_utc, released_at_utc)
                    VALUES
                        ('66666666-0000-0000-0000-000000000001', '44444444-0000-0000-0000-000000000001', hall, d + 10, d + 10, '14:00', '18:00', 'Provisional', 'Active',   n,                      NULL),
                        ('66666666-0000-0000-0000-000000000002', '44444444-0000-0000-0000-000000000002', hall, d + 14, d + 14, '09:00', '15:00', 'Provisional', 'Active',   n - interval '3 hours', NULL),
                        ('66666666-0000-0000-0000-000000000003', '44444444-0000-0000-0000-000000000003', hall, d + 7,  d + 7,  '10:00', '14:00', 'Definitive',  'Active',   n - interval '3 days',  NULL),
                        ('66666666-0000-0000-0000-000000000004', '44444444-0000-0000-0000-000000000004', hall, d - 5,  d - 5,  '08:00', '16:00', 'Definitive',  'Active',   n - interval '12 days', NULL),
                        ('66666666-0000-0000-0000-000000000005', '44444444-0000-0000-0000-000000000005', hall, d + 20, d + 20, '10:00', '15:00', 'Definitive',  'Released', n - interval '9 days',  n - interval '8 days'),
                        ('66666666-0000-0000-0000-000000000006', '44444444-0000-0000-0000-000000000006', hall, d + 9,  d + 9,  '14:00', '17:00', 'Provisional', 'Released', n - interval '2 days',  n - interval '1 day');

                    -- Own equipment reserved by the blocks (E3 projector, E5 lights).
                    INSERT INTO hall_schedule.schedule_block_equipment (block_id, code, quantity) VALUES
                        ('66666666-0000-0000-0000-000000000003', 'PROJECTOR', 1),
                        ('66666666-0000-0000-0000-000000000005', 'LIGHTS', 1);
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM hall_schedule.schedule_blocks WHERE id::text LIKE '66666666-0000-0000-0000-00000000000_';
                DELETE FROM hall_schedule.hall_schedules WHERE hall_id = '11111111-1111-1111-1111-111111111111'
                    AND NOT EXISTS (SELECT 1 FROM hall_schedule.schedule_blocks b
                                    WHERE b.hall_id = hall_schedules.hall_id AND b.schedule_date = hall_schedules.date);
                """);
        }
    }
}
