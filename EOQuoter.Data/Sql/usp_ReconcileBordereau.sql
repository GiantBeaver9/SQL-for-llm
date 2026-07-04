-- Reconciliation: the MGA's bound book vs the carrier's statement for the period, joined on
-- external_reference_id — the shared key across systems. Policy number is OUR key; the carrier
-- may not have it, and a renumber would silently break the join.
--
-- FULL OUTER JOIN so both directions of "present here, missing there" surface, plus premium
-- mismatches on matched refs. Results are keyed by batch_id: each run writes its own result set.
CREATE OR ALTER PROCEDURE dbo.usp_ReconcileBordereau
    @batch_id      UNIQUEIDENTIFIER,
    @period        DATE,
    @rejects_ref   UNIQUEIDENTIFIER,
    @matched       INT OUTPUT,
    @discrepancies INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- Carrier rows we can't even join (no external ref) are per-item failures, not batch failures.
    INSERT INTO dbo.rejected_items (rejects_ref, batch_id, external_reference_id, reason, raw_payload, rejected_at)
    SELECT @rejects_ref, @batch_id, CONCAT('carrier_statement:', cs.id), 'MissingExternalReferenceId',
           (SELECT cs.id, cs.policy_number, cs.gross_premium FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           SYSDATETIMEOFFSET()
    FROM dbo.carrier_statements cs
    WHERE cs.period = @period AND (cs.external_reference_id IS NULL OR cs.external_reference_id = '');

    INSERT INTO dbo.reconciliation_results (batch_id, period, external_reference_id, category,
                                            mga_gross, carrier_gross, detail, created_at)
    SELECT @batch_id, @period,
           COALESCE(b.external_reference_id, cs.external_reference_id),
           CASE
               WHEN cs.id IS NULL THEN 'MissingAtCarrier'
               WHEN b.id IS NULL THEN 'MissingAtMga'
               WHEN b.gross_premium <> cs.gross_premium THEN 'PremiumMismatch'
               ELSE 'Matched'
           END,
           b.gross_premium, cs.gross_premium,
           CASE
               WHEN cs.id IS NULL THEN CONCAT('MGA reported ', b.policy_number, ' but the carrier statement has no matching external ref')
               WHEN b.id IS NULL THEN 'Carrier statement row has no matching MGA bordereau entry'
               WHEN b.gross_premium <> cs.gross_premium THEN CONCAT('Gross premium differs by ', FORMAT(b.gross_premium - cs.gross_premium, '0.00'))
               ELSE 'Tie-out clean'
           END,
           SYSDATETIMEOFFSET()
    FROM (SELECT * FROM dbo.bordereau_entries WHERE period = @period) b
    FULL OUTER JOIN (SELECT * FROM dbo.carrier_statements
                     WHERE period = @period AND external_reference_id IS NOT NULL AND external_reference_id <> '') cs
        ON b.external_reference_id = cs.external_reference_id;

    SELECT @matched       = COUNT(CASE WHEN category = 'Matched' THEN 1 END),
           @discrepancies = COUNT(CASE WHEN category <> 'Matched' THEN 1 END)
    FROM dbo.reconciliation_results
    WHERE batch_id = @batch_id;

    COMMIT TRANSACTION;
END
