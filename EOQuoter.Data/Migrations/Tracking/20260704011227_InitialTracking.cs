using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOQuoter.Data.Migrations.Tracking
{
    /// <inheritdoc />
    public partial class InitialTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sproc_executions",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sproc_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    @params = table.Column<string>(name: "params", type: "nvarchar(max)", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    rows_affected = table.Column<long>(type: "bigint", nullable: false),
                    error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    rejects_ref = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sproc_executions", x => x.run_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sproc_executions_batch_id",
                table: "sproc_executions",
                column: "batch_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sproc_executions");
        }
    }
}
