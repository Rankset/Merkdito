using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merkdito.Data.Migrations;

/// <inheritdoc />
public partial class AddPasswordHashAndMovimentacoesEstoque : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PasswordHash",
            table: "Clientes",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MovimentacoesEstoque",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProdutoId = table.Column<int>(type: "int", nullable: false),
                EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
                Tipo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                Quantidade = table.Column<int>(type: "int", nullable: false),
                QuantidadeAnterior = table.Column<int>(type: "int", nullable: false),
                QuantidadePosterior = table.Column<int>(type: "int", nullable: false),
                Observacao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovimentacoesEstoque", x => x.Id);
                table.ForeignKey(
                    name: "FK_MovimentacoesEstoque_Estabelecimentos_EstabelecimentoId",
                    column: x => x.EstabelecimentoId,
                    principalTable: "Estabelecimentos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MovimentacoesEstoque_Produtos_ProdutoId",
                    column: x => x.ProdutoId,
                    principalTable: "Produtos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MovimentacoesEstoque_EstabelecimentoId_CriadoEmUtc",
            table: "MovimentacoesEstoque",
            columns: new[] { "EstabelecimentoId", "CriadoEmUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_MovimentacoesEstoque_ProdutoId_CriadoEmUtc",
            table: "MovimentacoesEstoque",
            columns: new[] { "ProdutoId", "CriadoEmUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MovimentacoesEstoque");
        migrationBuilder.DropColumn(name: "PasswordHash", table: "Clientes");
    }
}
