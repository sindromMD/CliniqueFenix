using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinique2000_DataAccess.Migrations
{
    public partial class DashboardAvailabilityAndCancellations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NbAnnulationsPatient",
                table: "ListeAttentes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ReservationsActivees",
                table: "ListeAttentes",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NbAnnulationsPatient",
                table: "ListeAttentes");

            migrationBuilder.DropColumn(
                name: "ReservationsActivees",
                table: "ListeAttentes");
        }
    }
}
