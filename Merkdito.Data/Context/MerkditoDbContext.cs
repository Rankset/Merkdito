
using Merkdito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Data.Context;

public class MerkditoDbContext : DbContext
{
    public MerkditoDbContext(
        DbContextOptions<MerkditoDbContext> options)
        : base(options)
    {
    }

    public DbSet<Cliente> Clientes => Set<Cliente>();

    public DbSet<Estabelecimento> Estabelecimentos
        => Set<Estabelecimento>();

    public DbSet<Categoria> Categorias => Set<Categoria>();

    public DbSet<Produto> Produtos => Set<Produto>();

    public DbSet<MovimentacaoEstoque> MovimentacoesEstoque => Set<MovimentacaoEstoque>();

    public DbSet<Venda> Vendas => Set<Venda>();

    public DbSet<ItemVenda> ItensVenda => Set<ItemVenda>();

    public DbSet<ProdutoEstabelecimento> ProdutosEstabelecimentos => Set<ProdutoEstabelecimento>();
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<Compra> Compras => Set<Compra>();
    public DbSet<ItemCompra> ItensCompra => Set<ItemCompra>();
    public DbSet<UsuarioFuncionario> UsuariosFuncionarios => Set<UsuarioFuncionario>();
    public DbSet<UsuarioFuncionarioEstabelecimento> UsuariosFuncionariosEstabelecimentos => Set<UsuarioFuncionarioEstabelecimento>();

    public DbSet<Consumidor> Consumidores => Set<Consumidor>();
    public DbSet<ConsumidorEstabelecimento> ConsumidorEstabelecimentos => Set<ConsumidorEstabelecimento>();
    public DbSet<PedidoConsumidor> PedidosConsumidores => Set<PedidoConsumidor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.Property(c => c.Nome)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(c => c.Email)
                .HasMaxLength(254)
                .IsRequired();

            entity.Property(c => c.Telefone)
                .HasMaxLength(30);

            entity.Property(c => c.PasswordHash)
                .HasMaxLength(500);
        });

        modelBuilder.Entity<Estabelecimento>(entity =>
        {
            entity.Property(e => e.NomeFantasia)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(e => e.Cnpj)
                .HasMaxLength(18);
            entity.Property(e => e.Endereco).HasMaxLength(250);
            entity.Property(e => e.Telefone).HasMaxLength(30);
            entity.HasIndex(e => new { e.ClienteId, e.EhEstoqueCentral })
                .IsUnique().HasFilter("[EhEstoqueCentral] = 1");

            entity.HasOne(e => e.Cliente)
                .WithMany(c => c.Estabelecimentos)
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.Property(c => c.Nome)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasOne(c => c.Cliente)
                .WithMany(c => c.Categorias)
                .HasForeignKey(c => c.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Estabelecimento)
                .WithMany(e => e.Categorias)
                .HasForeignKey(c => c.EstabelecimentoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Produto>(entity =>
        {
            entity.Property(p => p.Nome)
                .HasMaxLength(150)
                .IsRequired();

            entity.HasOne(p => p.Cliente)
                .WithMany(c => c.Produtos)
                .HasForeignKey(p => p.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(p => p.Preco)
                .HasPrecision(10, 2);

            entity.HasIndex(p => p.CodigoBarras)
                .IsUnique()
                .HasFilter("[CodigoBarras] IS NOT NULL");

            entity.HasOne(p => p.Categoria)
                .WithMany(c => c.Produtos)
                .HasForeignKey(p => p.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.Estabelecimento)
                .WithMany(e => e.Produtos)
                .HasForeignKey(p => p.EstabelecimentoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Venda>(entity =>
        {
            entity.ToTable("Vendas");
            entity.Property(v => v.IdVendaExterna).HasMaxLength(100).IsRequired();
            entity.Property(v => v.Total).HasPrecision(12, 2);
            entity.Property(v => v.Status).HasMaxLength(20).IsRequired();
            entity.Property(v => v.Origem).HasMaxLength(30).IsRequired();
            entity.Property(v => v.DataVendaUtc).IsRequired();
            entity.Property(v => v.CriadoEmUtc).IsRequired();
            entity.HasIndex(v => new { v.EstabelecimentoId, v.IdVendaExterna }).IsUnique();
            entity.HasIndex(v => new { v.EstabelecimentoId, v.DataVendaUtc });
            entity.HasOne(v => v.Estabelecimento)
                .WithMany(e => e.Vendas)
                .HasForeignKey(v => v.EstabelecimentoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ItemVenda>(entity =>
        {
            entity.ToTable("ItensVenda");
            entity.Property(i => i.NomeProduto).HasMaxLength(150).IsRequired();
            entity.Property(i => i.CodigoBarrasProduto).HasMaxLength(100);
            entity.Property(i => i.PrecoUnitario).HasPrecision(12, 2);
            entity.Property(i => i.TotalItem).HasPrecision(12, 2);
            entity.HasOne(i => i.Venda)
                .WithMany(v => v.Itens)
                .HasForeignKey(i => i.VendaId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(i => i.Produto)
                .WithMany(p => p.ItensVenda)
                .HasForeignKey(i => i.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(i => i.VendaId);
            entity.HasIndex(i => i.ProdutoId);
        });

        modelBuilder.Entity<ProdutoEstabelecimento>(entity =>
        {
            entity.ToTable("ProdutosEstabelecimentos");
            entity.Property(x => x.PrecoVenda).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.QuantidadeEstoque).IsRequired();
            entity.Property(x => x.EstoqueMinimo).IsRequired();
            entity.Property(x => x.Ativo).IsRequired();
            entity.Property(x => x.CriadoEmUtc).IsRequired();
            entity.HasIndex(x => new { x.ProdutoId, x.EstabelecimentoId }).IsUnique();
            entity.HasIndex(x => new { x.EstabelecimentoId, x.Ativo });
            entity.HasOne(x => x.Produto)
                .WithMany(p => p.ProdutosEstabelecimentos)
                .HasForeignKey(x => x.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Estabelecimento)
                .WithMany(e => e.ProdutosEstabelecimentos)
                .HasForeignKey(x => x.EstabelecimentoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Fornecedor>(entity =>
        {
            entity.ToTable("Fornecedores");
            entity.Property(f => f.Nome).HasMaxLength(150).IsRequired();
            entity.Property(f => f.Cnpj).HasMaxLength(18);
            entity.Property(f => f.Email).HasMaxLength(254);
            entity.Property(f => f.Telefone).HasMaxLength(30);
            entity.Property(f => f.Endereco).HasMaxLength(250);
            entity.Property(f => f.CriadoEmUtc).IsRequired();
            entity.HasIndex(f => new { f.ClienteId, f.Nome });
            entity.HasIndex(f => new { f.ClienteId, f.Cnpj }).IsUnique()
                .HasFilter("[Cnpj] IS NOT NULL");
            entity.HasOne(f => f.Cliente).WithMany(c => c.Fornecedores)
                .HasForeignKey(f => f.ClienteId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Compra>(entity =>
        {
            entity.ToTable("Compras");
            entity.Property(c => c.NumeroDocumento).HasMaxLength(80);
            entity.Property(c => c.Status).HasMaxLength(20).IsRequired();
            entity.Property(c => c.Observacao).HasMaxLength(500);
            entity.Property(c => c.Total).HasPrecision(12, 2);
            entity.Property(c => c.DataCompraUtc).IsRequired();
            entity.Property(c => c.CriadoEmUtc).IsRequired();
            entity.HasIndex(c => new { c.ClienteId, c.CriadoEmUtc });
            entity.HasIndex(c => new { c.EstabelecimentoId, c.Status });
            entity.HasOne(c => c.Cliente).WithMany(x => x.Compras)
                .HasForeignKey(c => c.ClienteId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.Fornecedor).WithMany(f => f.Compras)
                .HasForeignKey(c => c.FornecedorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.Estabelecimento).WithMany(e => e.Compras)
                .HasForeignKey(c => c.EstabelecimentoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ItemCompra>(entity =>
        {
            entity.ToTable("ItensCompra");
            entity.Property(i => i.NomeProduto).HasMaxLength(150).IsRequired();
            entity.Property(i => i.CodigoBarrasProduto).HasMaxLength(100);
            entity.Property(i => i.CustoUnitario).HasPrecision(12, 2);
            entity.Property(i => i.TotalItem).HasPrecision(12, 2);
            entity.HasOne(i => i.Compra).WithMany(c => c.Itens)
                .HasForeignKey(i => i.CompraId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(i => i.Produto).WithMany(p => p.ItensCompra)
                .HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(i => i.CompraId);
            entity.HasIndex(i => i.ProdutoId);
        });


        modelBuilder.Entity<UsuarioFuncionario>(entity =>
        {
            entity.ToTable("UsuariosFuncionarios");
            entity.Property(u => u.Nome).HasMaxLength(150).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(254).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(u => u.CriadoEmUtc).IsRequired();
            entity.HasIndex(u => new { u.ClienteId, u.Email }).IsUnique();
            entity.HasOne(u => u.Cliente).WithMany(c => c.UsuariosFuncionarios)
                .HasForeignKey(u => u.ClienteId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UsuarioFuncionarioEstabelecimento>(entity =>
        {
            entity.ToTable("UsuariosFuncionariosEstabelecimentos");
            entity.HasIndex(x => new { x.UsuarioFuncionarioId, x.EstabelecimentoId }).IsUnique();
            entity.HasIndex(x => x.EstabelecimentoId);
            entity.HasOne(x => x.UsuarioFuncionario).WithMany(u => u.Acessos)
                .HasForeignKey(x => x.UsuarioFuncionarioId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Estabelecimento).WithMany(e => e.AcessosFuncionarios)
                .HasForeignKey(x => x.EstabelecimentoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Consumidor>(entity => {
            entity.ToTable("Consumidores");
            entity.Property(x => x.Nome).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Ativo).IsRequired();
            entity.Property(x => x.CriadoEmUtc).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasOne(x => x.Estabelecimento).WithMany()
                .HasForeignKey(x => x.EstabelecimentoId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ConsumidorEstabelecimento>(entity => {
            entity.ToTable("ConsumidorEstabelecimentos");
            entity.Property(x => x.CriadoEmUtc).IsRequired();
            entity.HasIndex(x => new { x.ConsumidorId, x.EstabelecimentoId }).IsUnique();
            entity.HasIndex(x => x.EstabelecimentoId);
            entity.HasOne(x => x.Consumidor).WithMany(c => c.LojasVinculadas)
                .HasForeignKey(x => x.ConsumidorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Estabelecimento).WithMany()
                .HasForeignKey(x => x.EstabelecimentoId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PedidoConsumidor>(entity => {
            entity.ToTable("PedidosConsumidores");
            entity.Property(x => x.ChaveIdempotencia).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResumoItensSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CriadoEmUtc).IsRequired();
            entity.HasIndex(x => new { x.ConsumidorId, x.ChaveIdempotencia }).IsUnique();
            entity.HasIndex(x => x.VendaId).IsUnique();
            entity.HasOne(x => x.Consumidor).WithMany(x => x.Pedidos)
                .HasForeignKey(x => x.ConsumidorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Venda).WithMany().HasForeignKey(x => x.VendaId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Estabelecimento>().WithMany()
                .HasForeignKey(x => x.EstabelecimentoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MovimentacaoEstoque>(entity =>
        {
            entity.ToTable("MovimentacoesEstoque");
            entity.Property(m => m.Tipo).HasMaxLength(10).IsRequired();
            entity.Property(m => m.Observacao).HasMaxLength(300);
            entity.Property(m => m.CriadoEmUtc).IsRequired();

            entity.HasOne(m => m.Produto)
                .WithMany(p => p.MovimentacoesEstoque)
                .HasForeignKey(m => m.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.Estabelecimento)
                .WithMany(e => e.MovimentacoesEstoque)
                .HasForeignKey(m => m.EstabelecimentoId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(m => new { m.EstabelecimentoId, m.CriadoEmUtc });
            entity.HasIndex(m => new { m.ProdutoId, m.CriadoEmUtc });
        });
    }
}
