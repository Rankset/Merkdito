using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merkdito.Data.Migrations;

public partial class AddFornecedoresECompras : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Fornecedores",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ClienteId = table.Column<int>(type: "int", nullable: false),
                Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Cnpj = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                Telefone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                Endereco = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                Ativo = table.Column<bool>(type: "bit", nullable: false),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Fornecedores", x => x.Id);
                table.ForeignKey("FK_Fornecedores_Clientes_ClienteId", x => x.ClienteId,
                    principalTable: "Clientes", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Compras",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ClienteId = table.Column<int>(type: "int", nullable: false),
                FornecedorId = table.Column<int>(type: "int", nullable: false),
                EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
                NumeroDocumento = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                DataCompraUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Observacao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                RecebidaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Compras", x => x.Id);
                table.ForeignKey("FK_Compras_Clientes_ClienteId", x => x.ClienteId,
                    principalTable: "Clientes", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Compras_Fornecedores_FornecedorId", x => x.FornecedorId,
                    principalTable: "Fornecedores", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Compras_Estabelecimentos_EstabelecimentoId", x => x.EstabelecimentoId,
                    principalTable: "Estabelecimentos", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ItensCompra",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CompraId = table.Column<int>(type: "int", nullable: false),
                ProdutoId = table.Column<int>(type: "int", nullable: false),
                NomeProduto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                CodigoBarrasProduto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Quantidade = table.Column<int>(type: "int", nullable: false),
                CustoUnitario = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                TotalItem = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItensCompra", x => x.Id);
                table.ForeignKey("FK_ItensCompra_Compras_CompraId", x => x.CompraId,
                    principalTable: "Compras", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ItensCompra_Produtos_ProdutoId", x => x.ProdutoId,
                    principalTable: "Produtos", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_Fornecedores_ClienteId_Nome", "Fornecedores",
            new[] { "ClienteId", "Nome" });
        migrationBuilder.CreateIndex("IX_Fornecedores_ClienteId_Cnpj", "Fornecedores",
            new[] { "ClienteId", "Cnpj" }, unique: true, filter: "[Cnpj] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_Compras_ClienteId_CriadoEmUtc", "Compras",
            new[] { "ClienteId", "CriadoEmUtc" });
        migrationBuilder.CreateIndex("IX_Compras_EstabelecimentoId_Status", "Compras",
            new[] { "EstabelecimentoId", "Status" });
        migrationBuilder.CreateIndex("IX_Compras_FornecedorId", "Compras", "FornecedorId");
        migrationBuilder.CreateIndex("IX_ItensCompra_CompraId", "ItensCompra", "CompraId");
        migrationBuilder.CreateIndex("IX_ItensCompra_ProdutoId", "ItensCompra", "ProdutoId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ItensCompra");
        migrationBuilder.DropTable(name: "Compras");
        migrationBuilder.DropTable(name: "Fornecedores");
    }
}
