using Merkdito.Data.Context;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merkdito.Data.Migrations;

public partial class AddCentralWarehouseAndStoreInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EhEstoqueCentral", table: "Estabelecimentos", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(
            name: "Endereco", table: "Estabelecimentos", type: "nvarchar(250)", maxLength: 250, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Telefone", table: "Estabelecimentos", type: "nvarchar(30)", maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ClienteId", table: "Produtos", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ClienteId", table: "Categorias", type: "int", nullable: true);

        // Toda conta recebe um depósito central. Contas existentes sem loja ganham um depósito vazio.
        migrationBuilder.Sql(@"
INSERT INTO [Estabelecimentos] ([ClienteId], [NomeFantasia], [Cnpj], [Ativo], [CriadoEm], [EhEstoqueCentral])
SELECT c.[Id], N'Estoque Central', NULL, 1, SYSUTCDATETIME(), 1
FROM [Clientes] c
WHERE NOT EXISTS (SELECT 1 FROM [Estabelecimentos] e WHERE e.[ClienteId] = c.[Id]);

;WITH [Ordenadas] AS
(
    SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [ClienteId] ORDER BY [Id]) AS [Ordem]
    FROM [Estabelecimentos]
)
UPDATE e SET [EhEstoqueCentral] = CASE WHEN o.[Ordem] = 1 THEN 1 ELSE 0 END
FROM [Estabelecimentos] e
JOIN [Ordenadas] o ON o.[Id] = e.[Id];

UPDATE c SET [ClienteId] = e.[ClienteId]
FROM [Categorias] c
JOIN [Estabelecimentos] e ON e.[Id] = c.[EstabelecimentoId]
WHERE c.[ClienteId] IS NULL;

UPDATE p SET [ClienteId] = COALESCE(e.[ClienteId], ec.[ClienteId])
FROM [Produtos] p
LEFT JOIN [Estabelecimentos] e ON e.[Id] = p.[EstabelecimentoId]
LEFT JOIN [Categorias] c ON c.[Id] = p.[CategoriaId]
LEFT JOIN [Estabelecimentos] ec ON ec.[Id] = c.[EstabelecimentoId]
WHERE p.[ClienteId] IS NULL;
");

        migrationBuilder.CreateIndex(
            name: "IX_Estabelecimentos_ClienteId_EhEstoqueCentral",
            table: "Estabelecimentos", columns: new[] { "ClienteId", "EhEstoqueCentral" },
            unique: true, filter: "[EhEstoqueCentral] = 1");
        migrationBuilder.CreateIndex(name: "IX_Produtos_ClienteId", table: "Produtos", column: "ClienteId");
        migrationBuilder.CreateIndex(name: "IX_Categorias_ClienteId", table: "Categorias", column: "ClienteId");

        migrationBuilder.AddForeignKey(
            name: "FK_Produtos_Clientes_ClienteId", table: "Produtos", column: "ClienteId",
            principalTable: "Clientes", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_Categorias_Clientes_ClienteId", table: "Categorias", column: "ClienteId",
            principalTable: "Clientes", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "ProdutosEstabelecimentos",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ProdutoId = table.Column<int>(type: "int", nullable: false),
                EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
                PrecoVenda = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                QuantidadeEstoque = table.Column<int>(type: "int", nullable: false),
                EstoqueMinimo = table.Column<int>(type: "int", nullable: false),
                Ativo = table.Column<bool>(type: "bit", nullable: false),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProdutosEstabelecimentos", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProdutosEstabelecimentos_Produtos_ProdutoId",
                    column: x => x.ProdutoId,
                    principalTable: "Produtos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProdutosEstabelecimentos_Estabelecimentos_EstabelecimentoId",
                    column: x => x.EstabelecimentoId,
                    principalTable: "Estabelecimentos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProdutosEstabelecimentos_ProdutoId_EstabelecimentoId",
            table: "ProdutosEstabelecimentos", columns: new[] { "ProdutoId", "EstabelecimentoId" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_ProdutosEstabelecimentos_EstabelecimentoId_Ativo",
            table: "ProdutosEstabelecimentos", columns: new[] { "EstabelecimentoId", "Ativo" });

        // Preserva o saldo legado por unidade; depois garante que o catálogo também exista no depósito central.
        migrationBuilder.Sql(@"
INSERT INTO [ProdutosEstabelecimentos]
    ([ProdutoId], [EstabelecimentoId], [PrecoVenda], [QuantidadeEstoque], [EstoqueMinimo], [Ativo], [CriadoEmUtc])
SELECT p.[Id], p.[EstabelecimentoId], p.[Preco], p.[QuantidadeEstoque], p.[EstoqueMinimo], p.[Ativo], SYSUTCDATETIME()
FROM [Produtos] p
JOIN [Estabelecimentos] e ON e.[Id] = p.[EstabelecimentoId]
WHERE p.[ClienteId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [ProdutosEstabelecimentos] pe WHERE pe.[ProdutoId] = p.[Id] AND pe.[EstabelecimentoId] = p.[EstabelecimentoId]);

INSERT INTO [ProdutosEstabelecimentos]
    ([ProdutoId], [EstabelecimentoId], [PrecoVenda], [QuantidadeEstoque], [EstoqueMinimo], [Ativo], [CriadoEmUtc])
SELECT p.[Id], central.[Id], p.[Preco], CASE WHEN p.[EstabelecimentoId] IS NULL THEN p.[QuantidadeEstoque] ELSE 0 END, p.[EstoqueMinimo], p.[Ativo], SYSUTCDATETIME()
FROM [Produtos] p
JOIN [Estabelecimentos] central ON central.[ClienteId] = p.[ClienteId] AND central.[EhEstoqueCentral] = 1
WHERE p.[ClienteId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [ProdutosEstabelecimentos] pe WHERE pe.[ProdutoId] = p.[Id] AND pe.[EstabelecimentoId] = central.[Id]);

-- Mantém Produto como cadastro mestre e seus campos de compatibilidade sincronizados com o estoque central.
UPDATE p
SET p.[EstabelecimentoId] = central.[Id],
    p.[Preco] = pe.[PrecoVenda],
    p.[QuantidadeEstoque] = pe.[QuantidadeEstoque],
    p.[EstoqueMinimo] = pe.[EstoqueMinimo]
FROM [Produtos] p
JOIN [Estabelecimentos] central ON central.[ClienteId] = p.[ClienteId] AND central.[EhEstoqueCentral] = 1
JOIN [ProdutosEstabelecimentos] pe ON pe.[ProdutoId] = p.[Id] AND pe.[EstabelecimentoId] = central.[Id]
WHERE p.[ClienteId] IS NOT NULL;

-- Categorias pertencem ao catálogo do cliente, não a uma loja de venda específica.
UPDATE c SET c.[EstabelecimentoId] = central.[Id]
FROM [Categorias] c
JOIN [Estabelecimentos] central ON central.[ClienteId] = c.[ClienteId] AND central.[EhEstoqueCentral] = 1
WHERE c.[ClienteId] IS NOT NULL;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Down remove os estoques por loja; faça backup antes de executar rollback em ambiente real.
        migrationBuilder.DropTable(name: "ProdutosEstabelecimentos");
        migrationBuilder.DropForeignKey(name: "FK_Produtos_Clientes_ClienteId", table: "Produtos");
        migrationBuilder.DropForeignKey(name: "FK_Categorias_Clientes_ClienteId", table: "Categorias");
        migrationBuilder.DropIndex(name: "IX_Estabelecimentos_ClienteId_EhEstoqueCentral", table: "Estabelecimentos");
        migrationBuilder.DropIndex(name: "IX_Produtos_ClienteId", table: "Produtos");
        migrationBuilder.DropIndex(name: "IX_Categorias_ClienteId", table: "Categorias");
        migrationBuilder.DropColumn(name: "EhEstoqueCentral", table: "Estabelecimentos");
        migrationBuilder.DropColumn(name: "Endereco", table: "Estabelecimentos");
        migrationBuilder.DropColumn(name: "Telefone", table: "Estabelecimentos");
        migrationBuilder.DropColumn(name: "ClienteId", table: "Produtos");
        migrationBuilder.DropColumn(name: "ClienteId", table: "Categorias");
    }
}
