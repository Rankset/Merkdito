using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace Merkdito.Data.Migrations;

public partial class AddUsuariosFuncionariosEPermissoes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UsuariosFuncionarios",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ClienteId = table.Column<int>(type: "int", nullable: false),
                Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Ativo = table.Column<bool>(type: "bit", nullable: false),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                AtualizadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UsuariosFuncionarios", x => x.Id);
                table.ForeignKey("FK_UsuariosFuncionarios_Clientes_ClienteId", x => x.ClienteId, "Clientes", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UsuariosFuncionariosEstabelecimentos",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UsuarioFuncionarioId = table.Column<int>(type: "int", nullable: false),
                EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
                PodeVerProdutos = table.Column<bool>(type: "bit", nullable: false),
                PodeEditarProdutos = table.Column<bool>(type: "bit", nullable: false),
                PodeGerenciarEstoque = table.Column<bool>(type: "bit", nullable: false),
                PodeRegistrarVendas = table.Column<bool>(type: "bit", nullable: false),
                PodeVerRelatorios = table.Column<bool>(type: "bit", nullable: false),
                PodeGerenciarCompras = table.Column<bool>(type: "bit", nullable: false),
                PodeGerenciarFornecedores = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UsuariosFuncionariosEstabelecimentos", x => x.Id);
                table.ForeignKey("FK_UsuariosFuncionariosEstabelecimentos_Estabelecimentos_EstabelecimentoId", x => x.EstabelecimentoId, "Estabelecimentos", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_UsuariosFuncionariosEstabelecimentos_UsuariosFuncionarios_UsuarioFuncionarioId", x => x.UsuarioFuncionarioId, "UsuariosFuncionarios", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_UsuariosFuncionarios_ClienteId_Email", "UsuariosFuncionarios", new[] { "ClienteId", "Email" }, unique: true);
        migrationBuilder.CreateIndex("IX_UsuariosFuncionariosEstabelecimentos_EstabelecimentoId", "UsuariosFuncionariosEstabelecimentos", "EstabelecimentoId");
        migrationBuilder.CreateIndex("IX_UsuariosFuncionariosEstabelecimentos_UsuarioFuncionarioId_EstabelecimentoId", "UsuariosFuncionariosEstabelecimentos", new[] { "UsuarioFuncionarioId", "EstabelecimentoId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UsuariosFuncionariosEstabelecimentos");
        migrationBuilder.DropTable(name: "UsuariosFuncionarios");
    }
}
