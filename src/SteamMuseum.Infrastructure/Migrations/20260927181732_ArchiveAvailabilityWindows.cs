using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteamMuseum.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveAvailabilityWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "AvailabilityWindow",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "AvailabilityWindow");
        }
    }
}
