using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodo.Quotes.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Demo quotes (business-rules.md §8). D is today in La Paz. Totals by hand, BR-06..08:
    ///
    ///   Q1  Ana (Flexible 5 %)      D+12 09-13, 40 pers.  4 h x 500 x 0.90 = 1.800 - 90                = 1.710   Issued
    ///   Q2  Carlos (expired plan)   D+8  10-12, 30 pers.  2 h x 500 x 1.00 = 1.000 - 0                 = 1.000   Expired (issued D-6)
    ///   Q3  Fundación (external)    D+10 14-18, 60 pers.  4 h x 500 x 0.90 = 1.800 - 0                 = 1.800   Accepted -> E1
    ///   Q4  TechSol (Equipo 10 %)   D+14 09-15, 70 pers.  6 h x 500 x 0.80 = 2.400 - 240               = 2.160   Accepted -> E2
    ///   Q5  Ana (5 %)               D+7  10-14, 35 pers.  1.800 - 90 + projector 150 + 35 coffee x 35  = 3.085   Accepted -> E3
    ///   Q6  TechSol (10 %)          D-5  08-16, 80 pers.  8 h x 500 x 0.70 = 2.800 - 280               = 2.520   Accepted -> E4
    ///   Q7  TechSol (10 %)          D+20 10-15, 50 pers.  5 h x 500 x 0.80 = 2.000 - 200 + lights 200  = 2.000   Accepted -> E5
    ///   Q8  Carlos (expired plan)   D+9  14-17, 25 pers.  3 h x 500 x 0.90 = 1.350 - 0                 = 1.350   Accepted -> E6
    ///
    /// Q8 is an addition to the spec's Q1..Q7: E6 (rejected) also needs the accepted quote it came from.
    /// Fixed ids: quote 33333333-0000-0000-0000-00000000000N.
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
                    ana uuid := '22222222-0000-0000-0000-000000000001';
                    techsol uuid := '22222222-0000-0000-0000-000000000002';
                    carlos uuid := '22222222-0000-0000-0000-000000000003';
                    fundacion uuid := '22222222-0000-0000-0000-000000000004';
                BEGIN
                    INSERT INTO quotes.quotes (
                        id, customer_id, hall_id, date, start_time, end_time, attendance,
                        base_hourly_rate_amount, base_hourly_rate_currency, hours, rate_factor,
                        hall_rental_amount, hall_rental_currency, membership_discount_percentage,
                        membership_discount_amount, membership_discount_currency,
                        extras_total_amount, extras_total_currency, total_amount, total_currency,
                        status, issued_at_utc, expires_at_utc, accepted_at_utc)
                    VALUES
                        ('33333333-0000-0000-0000-000000000001', ana,       hall, d + 12, '09:00', '13:00', 40, 500, 'BOB', 4, 0.90, 1800, 'BOB',  5,  90, 'BOB',    0, 'BOB', 1710, 'BOB', 'Issued',   n,                      n + interval '96 hours',                       NULL),
                        ('33333333-0000-0000-0000-000000000002', carlos,    hall, d + 8,  '10:00', '12:00', 30, 500, 'BOB', 2, 1.00, 1000, 'BOB',  0,   0, 'BOB',    0, 'BOB', 1000, 'BOB', 'Expired',  n - interval '6 days',  n - interval '6 days' + interval '96 hours',   NULL),
                        ('33333333-0000-0000-0000-000000000003', fundacion, hall, d + 10, '14:00', '18:00', 60, 500, 'BOB', 4, 0.90, 1800, 'BOB',  0,   0, 'BOB',    0, 'BOB', 1800, 'BOB', 'Accepted', n - interval '1 day',   n - interval '1 day' + interval '96 hours',    n - interval '1 minute'),
                        ('33333333-0000-0000-0000-000000000004', techsol,   hall, d + 14, '09:00', '15:00', 70, 500, 'BOB', 6, 0.80, 2400, 'BOB', 10, 240, 'BOB',    0, 'BOB', 2160, 'BOB', 'Accepted', n - interval '3 days',  n - interval '3 days' + interval '96 hours',   n - interval '3 hours'),
                        ('33333333-0000-0000-0000-000000000005', ana,       hall, d + 7,  '10:00', '14:00', 35, 500, 'BOB', 4, 0.90, 1800, 'BOB',  5,  90, 'BOB', 1375, 'BOB', 3085, 'BOB', 'Accepted', n - interval '4 days',  n - interval '4 days' + interval '96 hours',   n - interval '3 days'),
                        ('33333333-0000-0000-0000-000000000006', techsol,   hall, d - 5,  '08:00', '16:00', 80, 500, 'BOB', 8, 0.70, 2800, 'BOB', 10, 280, 'BOB',    0, 'BOB', 2520, 'BOB', 'Accepted', n - interval '12 days', n - interval '12 days' + interval '96 hours',  n - interval '11 days'),
                        ('33333333-0000-0000-0000-000000000007', techsol,   hall, d + 20, '10:00', '15:00', 50, 500, 'BOB', 5, 0.80, 2000, 'BOB', 10, 200, 'BOB',  200, 'BOB', 2000, 'BOB', 'Accepted', n - interval '10 days', n - interval '10 days' + interval '96 hours',  n - interval '9 days'),
                        ('33333333-0000-0000-0000-000000000008', carlos,    hall, d + 9,  '14:00', '17:00', 25, 500, 'BOB', 3, 0.90, 1350, 'BOB',  0,   0, 'BOB',    0, 'BOB', 1350, 'BOB', 'Accepted', n - interval '2 days',  n - interval '2 days' + interval '96 hours',   n - interval '1 day');

                    -- Extra services frozen in the quotes (unit cost captured when quoting, BR-08).
                    INSERT INTO quotes.quote_extra_services (quote_id, code, name, type, quantity, unit_cost_amount, unit_cost_currency) VALUES
                        ('33333333-0000-0000-0000-000000000005', 'PROJECTOR',    'Proyector',    'OwnEquipment',     1, 150, 'BOB'),
                        ('33333333-0000-0000-0000-000000000005', 'COFFEE_BREAK', 'Coffee break', 'ExternalCatering', 35,  35, 'BOB'),
                        ('33333333-0000-0000-0000-000000000007', 'LIGHTS',       'Kit de luces', 'OwnEquipment',     1, 200, 'BOB');
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM quotes.quotes WHERE id::text LIKE '33333333-0000-0000-0000-00000000000_';
                """);
        }
    }
}
