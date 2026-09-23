using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteamMuseum.Infrastructure.Migrations;

public partial class RetireLegacyCompetence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Preserve historical assessment evidence outside the application's active model.
        migrationBuilder.RenameTable(name: "Competence", newName: "ArchivedCompetence");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(name: "ArchivedCompetence", newName: "Competence");
    }
}
