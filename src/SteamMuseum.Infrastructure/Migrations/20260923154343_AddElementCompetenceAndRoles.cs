using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteamMuseum.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddElementCompetenceAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompetenceRoleId",
                table: "Duty",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompetenceElement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    LearningType = table.Column<int>(type: "int", nullable: false),
                    ReassessmentMonths = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetenceElement", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompetenceRole",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    BaseRoleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    RailwayId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LocomotiveId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetenceRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetenceRole_CompetenceRole_BaseRoleId",
                        column: x => x.BaseRoleId,
                        principalTable: "CompetenceRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetenceRole_Locomotive_LocomotiveId",
                        column: x => x.LocomotiveId,
                        principalTable: "Locomotive",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetenceRole_Railway_RailwayId",
                        column: x => x.RailwayId,
                        principalTable: "Railway",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ElementAssessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    MemberId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ElementId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    AssessedOn = table.Column<DateTime>(type: "date", nullable: false),
                    ReassessmentDue = table.Column<DateTime>(type: "date", nullable: false),
                    ReassessmentMonths = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Evidence = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    AssessedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevocationReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElementAssessment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElementAssessment_CompetenceElement_ElementId",
                        column: x => x.ElementId,
                        principalTable: "CompetenceElement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElementAssessment_Member_AssessedBy",
                        column: x => x.AssessedBy,
                        principalTable: "Member",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElementAssessment_Member_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Member",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RoleElement",
                columns: table => new
                {
                    ElementId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RoleId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleElement", x => new { x.RoleId, x.ElementId });
                    table.ForeignKey(
                        name: "FK_RoleElement_CompetenceElement_ElementId",
                        column: x => x.ElementId,
                        principalTable: "CompetenceElement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleElement_CompetenceRole_RoleId",
                        column: x => x.RoleId,
                        principalTable: "CompetenceRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Duty_CompetenceRoleId",
                table: "Duty",
                column: "CompetenceRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetenceRole_BaseRoleId",
                table: "CompetenceRole",
                column: "BaseRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetenceRole_LocomotiveId",
                table: "CompetenceRole",
                column: "LocomotiveId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetenceRole_RailwayId",
                table: "CompetenceRole",
                column: "RailwayId");

            migrationBuilder.CreateIndex(
                name: "IX_ElementAssessment_AssessedBy",
                table: "ElementAssessment",
                column: "AssessedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ElementAssessment_ElementId",
                table: "ElementAssessment",
                column: "ElementId");

            migrationBuilder.CreateIndex(
                name: "IX_ElementAssessment_MemberId_ElementId_Sequence",
                table: "ElementAssessment",
                columns: new[] { "MemberId", "ElementId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleElement_ElementId",
                table: "RoleElement",
                column: "ElementId");

            migrationBuilder.AddForeignKey(
                name: "FK_Duty_CompetenceRole_CompetenceRoleId",
                table: "Duty",
                column: "CompetenceRoleId",
                principalTable: "CompetenceRole",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Duty_CompetenceRole_CompetenceRoleId",
                table: "Duty");

            migrationBuilder.DropTable(
                name: "ElementAssessment");

            migrationBuilder.DropTable(
                name: "RoleElement");

            migrationBuilder.DropTable(
                name: "CompetenceElement");

            migrationBuilder.DropTable(
                name: "CompetenceRole");

            migrationBuilder.DropIndex(
                name: "IX_Duty_CompetenceRoleId",
                table: "Duty");

            migrationBuilder.DropColumn(
                name: "CompetenceRoleId",
                table: "Duty");
        }
    }
}
