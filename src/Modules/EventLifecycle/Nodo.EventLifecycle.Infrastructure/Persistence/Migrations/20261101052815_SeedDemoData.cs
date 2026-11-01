using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodo.EventLifecycle.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Demo events (business-rules.md §8). D is today in La Paz. Totals match the quotes of the Quotes seed.
    ///
    ///   E1  Q3  Fundación  D+10 14-18  60 pers.  total 1.800  Requested (waiting for Management)
    ///   E2  Q4  TechSol    D+14 09-15  70 pers.  total 2.160  ApprovedByManagement: 30 % = 648.00 required, 324.00 paid
    ///   E3  Q5  Ana        D+7  10-14  35 pers.  total 3.085  Confirmed: 20 % = 617.00 paid; projector + coffee break
    ///   E4  Q6  TechSol    D-5  08-16  80 pers.  total 2.520  Completed: 50 % = 1.260.00 paid, 1.260.00 settled at the end
    ///   E5  Q7  TechSol    D+20 10-15  50 pers.  total 2.000  Cancelled: 30 % = 600.00 paid -> 600 Bs of credit; lights
    ///   E6  Q8  Carlos     D+9  14-17  25 pers.  total 1.350  Rejected by Management
    ///
    /// TechSol's credit is 600 Bs (E5), ready for the reschedule demo. Fixed ids: event 44444444-0000-0000-0000-00000000000N.
    /// E1 is requested "now" so its decision deadline (end of the local day) is still ahead when the data is loaded.
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
                    INSERT INTO event_lifecycle.hall_events (
                        id, quote_id, customer_id, hall_id, date, start_time, end_time, attendance,
                        total_amount, total_currency, status,
                        deposit_percentage, deposit_required_amount, deposit_required_currency,
                        deposit_accredited_amount, deposit_accredited_currency, deposit_transaction_ids,
                        requested_at_utc, decision_deadline_utc, deposit_deadline_utc,
                        decided_by, decided_at_utc, decision_approved, rejection_reason,
                        credit_generated_amount, credit_generated_currency, reschedule_count, extra_services_assigned,
                        remaining_to_settle_amount, remaining_to_settle_currency)
                    VALUES
                        -- E1: requested now; Management must answer before the end of today (local).
                        ('44444444-0000-0000-0000-000000000001', '33333333-0000-0000-0000-000000000003', fundacion, hall, d + 10, '14:00', '18:00', 60,
                         1800, 'BOB', 'Requested',
                         NULL, NULL, NULL, NULL, NULL, NULL,
                         n, ((d + 1)::timestamp AT TIME ZONE 'America/La_Paz'), NULL,
                         NULL, NULL, NULL, NULL,
                         0, 'BOB', 0, false, NULL, NULL),
                        -- E2: approved with 30 % (648.00); half paid (324.00); 48 h from approval to cover it.
                        ('44444444-0000-0000-0000-000000000002', '33333333-0000-0000-0000-000000000004', techsol, hall, d + 14, '09:00', '15:00', 70,
                         2160, 'BOB', 'ApprovedByManagement',
                         30, 648, 'BOB', 324, 'BOB', ARRAY['TX-SEED-E2-1'],
                         n - interval '3 hours', ((((n - interval '3 hours') AT TIME ZONE 'America/La_Paz')::date + 1)::timestamp AT TIME ZONE 'America/La_Paz'),
                         n - interval '2 hours' + interval '48 hours',
                         'Lucía Paz', n - interval '2 hours', true, NULL,
                         0, 'BOB', 0, false, NULL, NULL),
                        -- E3: confirmed in firm; 20 % = 617.00 (3.085 x 20 %) fully paid.
                        ('44444444-0000-0000-0000-000000000003', '33333333-0000-0000-0000-000000000005', ana, hall, d + 7, '10:00', '14:00', 35,
                         3085, 'BOB', 'Confirmed',
                         20, 617, 'BOB', 617, 'BOB', ARRAY['TX-SEED-E3-1'],
                         n - interval '3 days' - interval '1 hour', ((((n - interval '3 days' - interval '1 hour') AT TIME ZONE 'America/La_Paz')::date + 1)::timestamp AT TIME ZONE 'America/La_Paz'), NULL,
                         'Lucía Paz', n - interval '3 days', true, NULL,
                         0, 'BOB', 0, true, NULL, NULL),
                        -- E4: completed; 50 % = 1.260.00 paid, the other 1.260.00 settled at the end (BR-19).
                        ('44444444-0000-0000-0000-000000000004', '33333333-0000-0000-0000-000000000006', techsol, hall, d - 5, '08:00', '16:00', 80,
                         2520, 'BOB', 'Completed',
                         50, 1260, 'BOB', 1260, 'BOB', ARRAY['TX-SEED-E4-1'],
                         n - interval '11 days' - interval '1 hour', ((((n - interval '11 days' - interval '1 hour') AT TIME ZONE 'America/La_Paz')::date + 1)::timestamp AT TIME ZONE 'America/La_Paz'), NULL,
                         'Lucía Paz', n - interval '11 days', true, NULL,
                         0, 'BOB', 0, true, 1260, 'BOB'),
                        -- E5: confirmed, then cancelled: everything paid (600.00 = 30 % of 2.000) became credit (BR-21).
                        ('44444444-0000-0000-0000-000000000005', '33333333-0000-0000-0000-000000000007', techsol, hall, d + 20, '10:00', '15:00', 50,
                         2000, 'BOB', 'Cancelled',
                         30, 600, 'BOB', 600, 'BOB', ARRAY['TX-SEED-E5-1'],
                         n - interval '9 days' - interval '1 hour', ((((n - interval '9 days' - interval '1 hour') AT TIME ZONE 'America/La_Paz')::date + 1)::timestamp AT TIME ZONE 'America/La_Paz'), NULL,
                         'Lucía Paz', n - interval '9 days', true, NULL,
                         600, 'BOB', 0, true, NULL, NULL),
                        -- E6: rejected by Management.
                        ('44444444-0000-0000-0000-000000000006', '33333333-0000-0000-0000-000000000008', carlos, hall, d + 9, '14:00', '17:00', 25,
                         1350, 'BOB', 'Rejected',
                         NULL, NULL, NULL, NULL, NULL, NULL,
                         n - interval '1 day' - interval '2 hours', ((((n - interval '1 day' - interval '2 hours') AT TIME ZONE 'America/La_Paz')::date + 1)::timestamp AT TIME ZONE 'America/La_Paz'), NULL,
                         'Lucía Paz', n - interval '1 day', false, 'El salón estará en mantenimiento esa fecha.',
                         0, 'BOB', 0, false, NULL, NULL);

                    INSERT INTO event_lifecycle.extra_service_items
                        (id, hall_event_id, type, code, description, quantity, unit_cost_amount, unit_cost_currency)
                    VALUES
                        ('55555555-0000-0000-0000-000000000001', '44444444-0000-0000-0000-000000000003', 'OwnEquipment',     'PROJECTOR',    'Proyector',    1, 150, 'BOB'),
                        ('55555555-0000-0000-0000-000000000002', '44444444-0000-0000-0000-000000000003', 'ExternalCatering', 'COFFEE_BREAK', 'Coffee break', 35, 35, 'BOB'),
                        ('55555555-0000-0000-0000-000000000003', '44444444-0000-0000-0000-000000000005', 'OwnEquipment',     'LIGHTS',       'Kit de luces', 1, 200, 'BOB');

                    -- Timeline of every event (the application appends one row per transition).
                    INSERT INTO event_lifecycle.hall_event_history (hall_event_id, status, occurred_at_utc, actor, note) VALUES
                        ('44444444-0000-0000-0000-000000000001', 'Requested', n, 'Cliente', 'Solicitud creada a partir de una cotización aceptada.'),

                        ('44444444-0000-0000-0000-000000000002', 'Requested', n - interval '3 hours', 'Cliente', 'Solicitud creada a partir de una cotización aceptada.'),
                        ('44444444-0000-0000-0000-000000000002', 'ApprovedByManagement', n - interval '2 hours', 'Lucía Paz', 'Aprobado con depósito de 30 % (648.00 BOB).'),

                        ('44444444-0000-0000-0000-000000000003', 'Requested', n - interval '3 days' - interval '1 hour', 'Cliente', 'Solicitud creada a partir de una cotización aceptada.'),
                        ('44444444-0000-0000-0000-000000000003', 'ApprovedByManagement', n - interval '3 days', 'Lucía Paz', 'Aprobado con depósito de 20 % (617.00 BOB).'),
                        ('44444444-0000-0000-0000-000000000003', 'Confirmed', n - interval '3 days' + interval '1 hour', 'Sistema', 'Depósito cubierto: evento confirmado en firme.'),

                        ('44444444-0000-0000-0000-000000000004', 'Requested', n - interval '11 days' - interval '1 hour', 'Cliente', 'Solicitud creada a partir de una cotización aceptada.'),
                        ('44444444-0000-0000-0000-000000000004', 'ApprovedByManagement', n - interval '11 days', 'Lucía Paz', 'Aprobado con depósito de 50 % (1260.00 BOB).'),
                        ('44444444-0000-0000-0000-000000000004', 'Confirmed', n - interval '11 days' + interval '1 hour', 'Sistema', 'Depósito cubierto: evento confirmado en firme.'),
                        ('44444444-0000-0000-0000-000000000004', 'InProgress', n - interval '5 days' - interval '7 hours', 'Host de turno', 'Evento iniciado.'),
                        ('44444444-0000-0000-0000-000000000004', 'Completed', n - interval '5 days' + interval '1 hour', 'Host de turno', 'Evento completado. Saldo a liquidar: 1260.00 BOB.'),

                        ('44444444-0000-0000-0000-000000000005', 'Requested', n - interval '9 days' - interval '1 hour', 'Cliente', 'Solicitud creada a partir de una cotización aceptada.'),
                        ('44444444-0000-0000-0000-000000000005', 'ApprovedByManagement', n - interval '9 days', 'Lucía Paz', 'Aprobado con depósito de 30 % (600.00 BOB).'),
                        ('44444444-0000-0000-0000-000000000005', 'Confirmed', n - interval '9 days' + interval '1 hour', 'Sistema', 'Depósito cubierto: evento confirmado en firme.'),
                        ('44444444-0000-0000-0000-000000000005', 'Cancelled', n - interval '8 days', 'Recepción Nodo', 'Cancelado: El cliente ya no lo necesita. Sin devolución de dinero: 600.00 BOB pasan íntegros a saldo a favor.'),

                        ('44444444-0000-0000-0000-000000000006', 'Requested', n - interval '1 day' - interval '2 hours', 'Cliente', 'Solicitud creada a partir de una cotización aceptada.'),
                        ('44444444-0000-0000-0000-000000000006', 'Rejected', n - interval '1 day', 'Lucía Paz', 'Rechazado: El salón estará en mantenimiento esa fecha.');

                    -- TechSol's credit: the 600 Bs of E5, ready for the reschedule demo (BR-22, BR-23).
                    INSERT INTO event_lifecycle.customer_credits (customer_id, balance_amount, balance_currency)
                    VALUES (techsol, 600, 'BOB');
                    INSERT INTO event_lifecycle.customer_credit_movements
                        (customer_id, type, event_id, reason, occurred_at_utc, amount_amount, amount_currency)
                    VALUES (techsol, 'Granted', '44444444-0000-0000-0000-000000000005', 'Cancelación del evento', n - interval '8 days', 600, 'BOB');
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM event_lifecycle.customer_credits WHERE customer_id = '22222222-0000-0000-0000-000000000002';
                DELETE FROM event_lifecycle.hall_events WHERE id::text LIKE '44444444-0000-0000-0000-00000000000_';
                """);
        }
    }
}
