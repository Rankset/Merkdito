using Merkdito.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Merkdito.Data.Migrations;

[DbContext(typeof(MerkditoDbContext))]
[Migration("20261109000000_AddConsumidorApp")]
public sealed class AddConsumidorApp : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(name: "Consumidores", columns: table => new {
            Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
            Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
            PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
            EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
            Ativo = table.Column<bool>(type: "bit", nullable: false),
            CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => {
            table.PrimaryKey("PK_Consumidores", x => x.Id);
            table.ForeignKey("FK_Consumidores_Estabelecimentos", x => x.EstabelecimentoId,
                "Estabelecimentos", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name: "IX_Consumidores_EstabelecimentoId", table: "Consumidores", column: "EstabelecimentoId");
        migrationBuilder.CreateIndex(name: "IX_Consumidores_Email", table: "Consumidores", column: "Email", unique: true);
        migrationBuilder.CreateTable(name: "PedidosConsumidores", columns: table => new {
            Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            ConsumidorId = table.Column<int>(type: "int", nullable: false),
            VendaId = table.Column<int>(type: "int", nullable: false),
            EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
            ChaveIdempotencia = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ResumoItensSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => {
            table.PrimaryKey("PK_PedidosConsumidores", x => x.Id);
            table.ForeignKey("FK_PedidosConsumidores_Consumidores", x => x.ConsumidorId,
                "Consumidores", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_PedidosConsumidores_Vendas", x => x.VendaId,
                "Vendas", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_PedidosConsumidores_Estabelecimentos", x => x.EstabelecimentoId,
                "Estabelecimentos", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name: "IX_PedidosConsumidores_ConsumidorId_ChaveIdempotencia",
            table: "PedidosConsumidores", columns: new[] { "ConsumidorId", "ChaveIdempotencia" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_PedidosConsumidores_VendaId", table: "PedidosConsumidores", column: "VendaId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_PedidosConsumidores_EstabelecimentoId", table: "PedidosConsumidores", column: "EstabelecimentoId");
    }
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable("PedidosConsumidores");
        migrationBuilder.DropTable("Consumidores");
    }
}
