-- Bordereau generation: set-based on purpose. EF's per-row materialization is the wrong shape for
-- period-wide aggregation; this is the sproc side of the deliberate sprocs-for-batch / EF-for-entity split.
--
-- Per-item failure WITHOUT a cursor: validate the set, route failures to rejected_items, commit the
-- valid rows in ONE insert. "Roll back the failed item" becomes "it never commits and is captured".
-- Resume = reprocess the rejects, never the period.
--
-- No logging in here. SQL Server has no autonomous transactions — an INSERT into a log table inside
-- a failing transaction rolls back with it. The worker logs around this call on a separate connection.
CREATE OR ALTER PROCEDURE dbo.usp_GenerateBordereau
    @batch_id    UNIQUEIDENTIFIER,
    @period      DATE,               -- first day of the bordereau month
    @rejects_ref UNIQUEIDENTIFIER,
    @inserted    INT OUTPUT,
    @rejected    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @period_end DATE = DATEADD(MONTH, 1, @period);

    BEGIN TRANSACTION;

    -- Candidate set: policies bound in the period, not yet reported for this period.
    SELECT p.policy_id, p.policy_number, p.external_reference_id, p.premium, p.commission_rate
    INTO #candidates
    FROM dbo.policies p
    WHERE p.bound_at >= @period AND p.bound_at < @period_end
      AND NOT EXISTS (
          SELECT 1 FROM dbo.bordereau_entries b
          WHERE b.period = @period AND b.external_reference_id = p.external_reference_id);

    -- Route invalid items to the rejects table; they never reach the bordereau.
    INSERT INTO dbo.rejected_items (rejects_ref, batch_id, external_reference_id, reason, raw_payload, rejected_at)
    SELECT @rejects_ref, @batch_id,
           COALESCE(NULLIF(c.external_reference_id, ''), CONCAT('policy:', c.policy_id)),
           CASE
               WHEN c.external_reference_id IS NULL OR c.external_reference_id = '' THEN 'MissingExternalReferenceId'
               WHEN c.commission_rate < 0 OR c.commission_rate >= 1 THEN 'CommissionRateOutOfRange'
               ELSE 'NonPositivePremium'
           END,
           (SELECT c.policy_id, c.policy_number, c.premium, c.commission_rate FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           SYSDATETIMEOFFSET()
    FROM #candidates c
    WHERE c.external_reference_id IS NULL OR c.external_reference_id = ''
       OR c.commission_rate < 0 OR c.commission_rate >= 1
       OR c.premium <= 0;

    SET @rejected = @@ROWCOUNT;

    -- Commit the valid set in one operation. Double-entry: net is derived as gross minus the
    -- rounded commission, so gross = commission + net holds to the cent on every row.
    INSERT INTO dbo.bordereau_entries (batch_id, period, policy_number, external_reference_id,
                                       gross_premium, commission, net_to_carrier, generated_at)
    SELECT @batch_id, @period, c.policy_number, c.external_reference_id,
           c.premium,
           ROUND(c.premium * c.commission_rate, 2),
           c.premium - ROUND(c.premium * c.commission_rate, 2),
           SYSDATETIMEOFFSET()
    FROM #candidates c
    WHERE NOT (c.external_reference_id IS NULL OR c.external_reference_id = ''
               OR c.commission_rate < 0 OR c.commission_rate >= 1
               OR c.premium <= 0);

    SET @inserted = @@ROWCOUNT;

    COMMIT TRANSACTION;
END
