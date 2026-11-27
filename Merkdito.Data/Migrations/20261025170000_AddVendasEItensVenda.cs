using Merkdito.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merkdito.Data.Migrations;

public partial class AddVendasEItensVenda : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Vendas",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
                IdVendaExterna = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DataVendaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Origem = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Vendas", x => x.Id);
                table.ForeignKey(
                    name: "FK_Vendas_Estabelecimentos_EstabelecimentoId",
                    column: x => x.EstabelecimentoId,
                    principalTable: "Estabelecimentos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ItensVenda",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                VendaId = table.Column<int>(type: "int", nullable: false),
                ProdutoId = table.Column<int>(type: "int", nullable: false),
                NomeProduto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                CodigoBarrasProduto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Quantidade = table.Column<int>(type: "int", nullable: false),
                PrecoUnitario = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                TotalItem = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItensVenda", x => x.Id);
                table.ForeignKey(
                    name: "FK_ItensVenda_Produtos_ProdutoId",
                    column: x => x.ProdutoId,
                    principalTable: "Produtos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItensVenda_Vendas_VendaId",
                    column: x => x.VendaId,
                    principalTable: "Vendas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Vendas_EstabelecimentoId_DataVendaUtc",
            table: "Vendas",
            columns: new[] { "EstabelecimentoId", "DataVendaUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_Vendas_EstabelecimentoId_IdVendaExterna",
            table: "Vendas",
            columns: new[] { "EstabelecimentoId", "IdVendaExterna" },
            unique: true);
        migrationBuilder.CreateIndex(name: "IX_ItensVenda_ProdutoId", table: "ItensVenda", column: "ProdutoId");
        migrationBuilder.CreateIndex(name: "IX_ItensVenda_VendaId", table: "ItensVenda", column: "VendaId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ItensVenda");
        migrationBuilder.DropTable(name: "Vendas");
    }
}
