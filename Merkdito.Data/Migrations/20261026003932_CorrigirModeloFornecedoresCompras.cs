using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merkdito.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrigirModeloFornecedoresCompras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Estabelecimentos_ClienteId",
                table: "Estabelecimentos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Estabelecimentos_ClienteId",
                table: "Estabelecimentos",
                column: "ClienteId");
        }
    }
}
