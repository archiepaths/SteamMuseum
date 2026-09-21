using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteamMuseum.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWindowDateRangesAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "AvailabilityWindow",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AvailabilityWindowDateRange",
                columns: table => new
                {
                    Start = table.Column<DateTime>(type: "date", nullable: false),
                    WindowId = table.Column<Guid>(type: "char(36)", nullable: false),
                    End = table.Column<DateTime>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityWindowDateRange", x => new { x.WindowId, x.Start });
                    table.ForeignKey(
                        name: "FK_AvailabilityWindowDateRange_AvailabilityWindow_WindowId",
                        column: x => x.WindowId,
                        principalTable: "AvailabilityWindow",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvailabilityWindowDateRange");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "AvailabilityWindow");
        }
    }
}
