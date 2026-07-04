using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOQuoter.Data.Migrations.Quoter
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appetite",
                columns: table => new
                {
                    profession_class = table.Column<int>(type: "int", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_written = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appetite", x => new { x.profession_class, x.effective_date });
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_reference_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    event_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    detail_json = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "batch_jobs",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    checkpoint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_jobs", x => x.job_id);
                });

            migrationBuilder.CreateTable(
                name: "bordereau_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    policy_number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    external_reference_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    gross_premium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    commission = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    net_to_carrier = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bordereau_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_statements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    external_reference_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    policy_number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    gross_premium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_statements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dead_letters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    outbox_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    dedupe_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    error = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dead_letters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delegated_authority",
                columns: table => new
                {
                    state = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    max_limit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delegated_authority", x => new { x.state, x.effective_date });
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    request_fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    response_snapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status_code = table.Column<int>(type: "int", nullable: true),
                    result_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "limit_factors",
                columns: table => new
                {
                    limit_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    factor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_limit_factors", x => new { x.limit_amount, x.effective_date });
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    dedupe_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    quote_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    premium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    commission_rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    external_reference_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    bound_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    forms_json = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policies", x => x.policy_id);
                });

            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    dedupe_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => x.dedupe_key);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    quote_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    submission_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    premium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    limit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    retention = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    subjectivities_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    quote_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    base_premium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    rate_effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ledger_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.quote_id);
                });

            migrationBuilder.CreateTable(
                name: "rate_table",
                columns: table => new
                {
                    profession_class = table.Column<int>(type: "int", nullable: false),
                    revenue_band = table.Column<int>(type: "int", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_table", x => new { x.profession_class, x.revenue_band, x.effective_date });
                });

            migrationBuilder.CreateTable(
                name: "recency_bands",
                columns: table => new
                {
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    age_floor_inclusive_years = table.Column<double>(type: "float", nullable: false),
                    age_ceiling_exclusive_years = table.Column<double>(type: "float", nullable: false),
                    weight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recency_bands", x => new { x.effective_date, x.label });
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    external_reference_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    mga_gross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    carrier_gross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    detail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rejected_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    rejects_ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_reference_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    raw_payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    rejected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reprocessed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rejected_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "retention_factors",
                columns: table => new
                {
                    retention_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    factor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_factors", x => new { x.retention_amount, x.effective_date });
                });

            migrationBuilder.CreateTable(
                name: "severity_bands",
                columns: table => new
                {
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    floor_inclusive = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ceiling_exclusive = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    load = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_severity_bands", x => new { x.effective_date, x.label });
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    raw_content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    referral_reason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    risk_profile_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    quote_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "underwriting_parameters",
                columns: table => new
                {
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    min_years_in_business = table.Column<int>(type: "int", nullable: false),
                    claims_knockout_count = table.Column<int>(type: "int", nullable: false),
                    claims_window_years = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_underwriting_parameters", x => x.effective_date);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity_id",
                table: "audit_events",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_external_reference_id",
                table: "audit_events",
                column: "external_reference_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_status",
                table: "batch_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_bordereau_entries_period_external_reference_id",
                table: "bordereau_entries",
                columns: new[] { "period", "external_reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_statements_period_external_reference_id",
                table: "carrier_statements",
                columns: new[] { "period", "external_reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_dispatched_at",
                table: "outbox",
                column: "dispatched_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_seq",
                table: "outbox",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_policies_external_reference_id",
                table: "policies",
                column: "external_reference_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_policies_policy_number",
                table: "policies",
                column: "policy_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_policies_quote_id",
                table: "policies",
                column: "quote_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_submission_id",
                table: "quotes",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_results_batch_id",
                table: "reconciliation_results",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_rejected_items_rejects_ref",
                table: "rejected_items",
                column: "rejects_ref");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appetite");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "batch_jobs");

            migrationBuilder.DropTable(
                name: "bordereau_entries");

            migrationBuilder.DropTable(
                name: "carrier_statements");

            migrationBuilder.DropTable(
                name: "dead_letters");

            migrationBuilder.DropTable(
                name: "delegated_authority");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "limit_factors");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "policies");

            migrationBuilder.DropTable(
                name: "processed_messages");

            migrationBuilder.DropTable(
                name: "quotes");

            migrationBuilder.DropTable(
                name: "rate_table");

            migrationBuilder.DropTable(
                name: "recency_bands");

            migrationBuilder.DropTable(
                name: "reconciliation_results");

            migrationBuilder.DropTable(
                name: "rejected_items");

            migrationBuilder.DropTable(
                name: "retention_factors");

            migrationBuilder.DropTable(
                name: "severity_bands");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "underwriting_parameters");
        }
    }
}
