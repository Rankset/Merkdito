using Merkdito.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Merkdito.Data.Migrations;

[DbContext(typeof(MerkditoDbContext))]
[Migration("20261112000000_AddVinculosConsumidorLoja")]
public sealed class AddVinculosConsumidorLoja : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "ConsumidorEstabelecimentos", columns: table => new {
            Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            ConsumidorId = table.Column<int>(type: "int", nullable: false),
            EstabelecimentoId = table.Column<int>(type: "int", nullable: false),
            CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => {
            table.PrimaryKey("PK_ConsumidorEstabelecimentos", x => x.Id);
            table.ForeignKey("FK_ConsumidorEstabelecimentos_Consumidores", x => x.ConsumidorId,
                "Consumidores", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_ConsumidorEstabelecimentos_Estabelecimentos", x => x.EstabelecimentoId,
                "Estabelecimentos", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name: "IX_ConsumidorEstabelecimentos_ConsumidorId_EstabelecimentoId",
            table: "ConsumidorEstabelecimentos", columns: new[] { "ConsumidorId", "EstabelecimentoId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ConsumidorEstabelecimentos_EstabelecimentoId",
            table: "ConsumidorEstabelecimentos", column: "EstabelecimentoId");
        migrationBuilder.Sql("INSERT INTO dbo.ConsumidorEstabelecimentos(ConsumidorId, EstabelecimentoId, CriadoEmUtc) SELECT Id, EstabelecimentoId, SYSUTCDATETIME() FROM dbo.Consumidores");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ConsumidorEstabelecimentos");
    }
}
