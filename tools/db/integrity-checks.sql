-- M13 database integrity checks (AC-13.5, scenario E2E-13). READ-ONLY: every statement is a SELECT.
-- One row per check: name | violations. Every count must be 0.
-- Used by the local E2E stack (after each run), DatabaseIntegrityTests (Testcontainers) and, read-only, on Supabase (D7).
-- Audit-gap checks only consider rows created after the first audit record, so data from before the audit trail
-- existed is not reported as a gap.

WITH audit_start AS (
    SELECT coalesce(min(occurred_at), '-infinity'::timestamptz) AS at FROM milkshake.audit_logs
)
SELECT check_name, violations FROM (

    -- Orphans and incomplete orders
    SELECT 1 AS n, 'orders_without_lines' AS check_name, count(*) AS violations
    FROM milkshake.orders o
    WHERE NOT EXISTS (SELECT 1 FROM milkshake.order_items i WHERE i.order_id = o.id)

    UNION ALL SELECT 2, 'lines_without_order', count(*)
    FROM milkshake.order_items i
    WHERE NOT EXISTS (SELECT 1 FROM milkshake.orders o WHERE o.id = i.order_id)

    UNION ALL SELECT 3, 'payments_without_order', count(*)
    FROM milkshake.payments p
    WHERE NOT EXISTS (SELECT 1 FROM milkshake.orders o WHERE o.id = p.order_id)

    UNION ALL SELECT 4, 'refunds_without_payment_of_the_same_order', count(*)
    FROM milkshake.refunds r
    WHERE NOT EXISTS (SELECT 1 FROM milkshake.payments p WHERE p.id = r.payment_id AND p.order_id = r.order_id)

    UNION ALL SELECT 5, 'emails_without_order', count(*)
    FROM milkshake.email_notifications e
    WHERE NOT EXISTS (SELECT 1 FROM milkshake.orders o WHERE o.id = e.order_id)

    -- Money and state consistency
    UNION ALL SELECT 6, 'confirmed_orders_without_succeeded_payment', count(*)
    FROM milkshake.orders o
    WHERE o.status = 'Confirmed'
      AND NOT EXISTS (SELECT 1 FROM milkshake.payments p WHERE p.order_id = o.id AND p.status = 'Succeeded')

    UNION ALL SELECT 7, 'succeeded_payments_on_unpaid_orders', count(*)
    FROM milkshake.payments p
    JOIN milkshake.orders o ON o.id = p.order_id
    WHERE p.status = 'Succeeded' AND o.status NOT IN ('Confirmed', 'Cancelled')

    UNION ALL SELECT 8, 'orders_with_more_than_one_succeeded_payment', count(*)
    FROM (SELECT order_id FROM milkshake.payments WHERE status = 'Succeeded' GROUP BY order_id HAVING count(*) > 1) d

    UNION ALL SELECT 9, 'succeeded_payment_amount_not_order_total', count(*)
    FROM milkshake.payments p
    JOIN milkshake.orders o ON o.id = p.order_id
    WHERE p.status = 'Succeeded' AND (p.amount_minor <> round(o.total * 100) OR p.currency_code <> o.currency_code)

    UNION ALL SELECT 10, 'paid_and_cancelled_orders_without_refund', count(*)
    FROM milkshake.orders o
    WHERE o.status = 'Cancelled'
      AND EXISTS (SELECT 1 FROM milkshake.payments p WHERE p.order_id = o.id AND p.status = 'Succeeded')
      AND NOT EXISTS (SELECT 1 FROM milkshake.refunds r WHERE r.order_id = o.id)

    UNION ALL SELECT 11, 'refunds_larger_than_their_payment', count(*)
    FROM milkshake.refunds r
    JOIN milkshake.payments p ON p.id = r.payment_id
    WHERE r.paid_amount_minor > p.amount_minor OR r.amount_minor > r.paid_amount_minor

    UNION ALL SELECT 12, 'payments_refunded_more_than_once_in_total', count(*)
    FROM (
        SELECT r.payment_id
        FROM milkshake.refunds r
        JOIN milkshake.payments p ON p.id = r.payment_id
        GROUP BY r.payment_id, p.amount_minor
        HAVING sum(r.amount_minor) > p.amount_minor
    ) d

    UNION ALL SELECT 13, 'order_subtotal_not_sum_of_lines', count(*)
    FROM milkshake.orders o
    WHERE o.subtotal <> (SELECT coalesce(sum(i.price), 0) FROM milkshake.order_items i WHERE i.order_id = o.id)

    -- Audit gaps: business rows without their Insert audit record (since the audit trail began)
    UNION ALL SELECT 14, 'orders_without_insert_audit', count(*)
    FROM milkshake.orders o, audit_start
    WHERE o.created_at >= audit_start.at
      AND NOT EXISTS (SELECT 1 FROM milkshake.audit_logs a
                      WHERE a.table_name = 'orders' AND a.action = 'Insert' AND a.record_id = o.id::text)

    UNION ALL SELECT 15, 'order_lines_without_insert_audit', count(*)
    FROM milkshake.order_items i
    JOIN milkshake.orders o ON o.id = i.order_id, audit_start
    WHERE o.created_at >= audit_start.at
      AND NOT EXISTS (SELECT 1 FROM milkshake.audit_logs a
                      WHERE a.table_name = 'order_items' AND a.action = 'Insert' AND a.record_id = i.id::text)

    UNION ALL SELECT 16, 'payments_without_insert_audit', count(*)
    FROM milkshake.payments p, audit_start
    WHERE p.created_at >= audit_start.at
      AND NOT EXISTS (SELECT 1 FROM milkshake.audit_logs a
                      WHERE a.table_name = 'payments' AND a.action = 'Insert' AND a.record_id = p.id::text)

    UNION ALL SELECT 17, 'refunds_without_insert_audit', count(*)
    FROM milkshake.refunds r, audit_start
    WHERE r.created_at >= audit_start.at
      AND NOT EXISTS (SELECT 1 FROM milkshake.audit_logs a
                      WHERE a.table_name = 'refunds' AND a.action = 'Insert' AND a.record_id = r.id::text)

    UNION ALL SELECT 18, 'orders_changed_after_insert_without_update_audit', count(*)
    FROM milkshake.orders o, audit_start
    WHERE o.created_at >= audit_start.at AND o.updated_at > o.created_at
      AND NOT EXISTS (SELECT 1 FROM milkshake.audit_logs a
                      WHERE a.table_name = 'orders' AND a.action = 'Update' AND a.record_id = o.id::text)
) checks
ORDER BY n;
