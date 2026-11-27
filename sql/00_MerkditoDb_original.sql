/*
===============================================================================
MERKDITO - SCRIPT COMPLETO DE BANCO DE DADOS
Baseado nas migrations do projeto Merkdito_Finalizado(5).zip
Banco padrão: MerkditoDb | SQL Server / SQL Server Express

IMPORTANTE:
- Execute em uma instância de desenvolvimento/teste do SQL Server Management Studio.
- O script NÃO apaga nem sobrescreve um banco existente.
- Se MerkditoDb já existir, o script interrompe a execução para evitar perda de dados.
- Este script cria a estrutura e registra as migrations no histórico do EF Core.
- Não cria usuário ADMIN/CLIENTE nem senhas iniciais; essas credenciais devem ser criadas
  pelo fluxo oficial da aplicação para que o hash de senha seja válido.
===============================================================================
*/

USE [master];
GO

IF DB_ID(N'MerkditoDb') IS NOT NULL
BEGIN
    THROW 51000, 'O banco MerkditoDb já existe. Execução cancelada para proteger os dados. Use um banco novo ou faça backup e confira o estado das migrations.', 1;
END;
GO

CREATE DATABASE [MerkditoDb];
GO

USE [MerkditoDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* 1. Categorias */
CREATE TABLE dbo.Categorias
(
    Id                 int IDENTITY(1,1) NOT NULL,
    Nome               nvarchar(100) NOT NULL,
    Descricao          nvarchar(max) NULL,
    Ativa              bit NOT NULL,
    EstabelecimentoId  int NULL,
    ClienteId          int NULL,
    CONSTRAINT PK_Categorias PRIMARY KEY CLUSTERED (Id)
);
GO

/* 2. Clientes */
CREATE TABLE dbo.Clientes
(
    Id           int IDENTITY(1,1) NOT NULL,
    Nome         nvarchar(150) NOT NULL,
    Email        nvarchar(254) NOT NULL,
    Telefone     nvarchar(30) NULL,
    Ativo        bit NOT NULL,
    CriadoEm     datetime2 NOT NULL,
    PasswordHash nvarchar(500) NULL,
    CONSTRAINT PK_Clientes PRIMARY KEY CLUSTERED (Id)
);
GO

/* 3. Estabelecimentos / lojas */
CREATE TABLE dbo.Estabelecimentos
(
    Id                 int IDENTITY(1,1) NOT NULL,
    ClienteId          int NOT NULL,
    NomeFantasia       nvarchar(150) NOT NULL,
    Cnpj               nvarchar(18) NULL,
    Ativo              bit NOT NULL,
    CriadoEm           datetime2 NOT NULL,
    EhEstoqueCentral   bit NOT NULL CONSTRAINT DF_Estabelecimentos_EhEstoqueCentral DEFAULT (0),
    Endereco           nvarchar(250) NULL,
    Telefone           nvarchar(30) NULL,
    CONSTRAINT PK_Estabelecimentos PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Estabelecimentos_Clientes_ClienteId
        FOREIGN KEY (ClienteId) REFERENCES dbo.Clientes(Id) ON DELETE NO ACTION
);
GO

/* 4. Produtos */
CREATE TABLE dbo.Produtos
(
    Id                 int IDENTITY(1,1) NOT NULL,
    Nome               nvarchar(150) NOT NULL,
    Descricao          nvarchar(max) NULL,
    CodigoBarras       nvarchar(450) NULL,
    Preco              decimal(10,2) NOT NULL,
    QuantidadeEstoque  int NOT NULL,
    EstoqueMinimo      int NOT NULL,
    Ativo              bit NOT NULL,
    CategoriaId        int NOT NULL,
    EstabelecimentoId  int NULL,
    ClienteId          int NULL,
    CONSTRAINT PK_Produtos PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Produtos_Categorias_CategoriaId
        FOREIGN KEY (CategoriaId) REFERENCES dbo.Categorias(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Produtos_Estabelecimentos_EstabelecimentoId
        FOREIGN KEY (EstabelecimentoId) REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Produtos_Clientes_ClienteId
        FOREIGN KEY (ClienteId) REFERENCES dbo.Clientes(Id) ON DELETE NO ACTION
);
GO

/* 5. Estoque por estabelecimento */
CREATE TABLE dbo.ProdutosEstabelecimentos
(
    Id                 int IDENTITY(1,1) NOT NULL,
    ProdutoId          int NOT NULL,
    EstabelecimentoId  int NOT NULL,
    PrecoVenda         decimal(10,2) NOT NULL,
    QuantidadeEstoque  int NOT NULL,
    EstoqueMinimo      int NOT NULL,
    Ativo              bit NOT NULL,
    CriadoEmUtc        datetime2 NOT NULL,
    CONSTRAINT PK_ProdutosEstabelecimentos PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ProdutosEstabelecimentos_Produtos_ProdutoId
        FOREIGN KEY (ProdutoId) REFERENCES dbo.Produtos(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_ProdutosEstabelecimentos_Estabelecimentos_EstabelecimentoId
        FOREIGN KEY (EstabelecimentoId) REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION
);
GO

/* 6. Movimentações de estoque */
CREATE TABLE dbo.MovimentacoesEstoque
(
    Id                  int IDENTITY(1,1) NOT NULL,
    ProdutoId           int NOT NULL,
    EstabelecimentoId   int NOT NULL,
    Tipo                nvarchar(10) NOT NULL,
    Quantidade          int NOT NULL,
    QuantidadeAnterior  int NOT NULL,
    QuantidadePosterior int NOT NULL,
    Observacao          nvarchar(300) NULL,
    CriadoEmUtc         datetime2 NOT NULL,
    CONSTRAINT PK_MovimentacoesEstoque PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_MovimentacoesEstoque_Estabelecimentos_EstabelecimentoId
        FOREIGN KEY (EstabelecimentoId) REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_MovimentacoesEstoque_Produtos_ProdutoId
        FOREIGN KEY (ProdutoId) REFERENCES dbo.Produtos(Id) ON DELETE NO ACTION
);
GO

/* 7. Vendas */
CREATE TABLE dbo.Vendas
(
    Id                 int IDENTITY(1,1) NOT NULL,
    EstabelecimentoId  int NOT NULL,
    IdVendaExterna     nvarchar(100) NOT NULL,
    DataVendaUtc       datetime2 NOT NULL,
    Total              decimal(12,2) NOT NULL,
    Status             nvarchar(20) NOT NULL,
    Origem             nvarchar(30) NOT NULL,
    CriadoEmUtc        datetime2 NOT NULL,
    CONSTRAINT PK_Vendas PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Vendas_Estabelecimentos_EstabelecimentoId
        FOREIGN KEY (EstabelecimentoId) REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION
);
GO

/* 8. Itens da venda */
CREATE TABLE dbo.ItensVenda
(
    Id                  int IDENTITY(1,1) NOT NULL,
    VendaId             int NOT NULL,
    ProdutoId           int NOT NULL,
    NomeProduto         nvarchar(150) NOT NULL,
    CodigoBarrasProduto nvarchar(100) NULL,
    Quantidade          int NOT NULL,
    PrecoUnitario       decimal(12,2) NOT NULL,
    TotalItem           decimal(12,2) NOT NULL,
    CONSTRAINT PK_ItensVenda PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ItensVenda_Produtos_ProdutoId
        FOREIGN KEY (ProdutoId) REFERENCES dbo.Produtos(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_ItensVenda_Vendas_VendaId
        FOREIGN KEY (VendaId) REFERENCES dbo.Vendas(Id) ON DELETE NO ACTION
);
GO

/* 9. Fornecedores */
CREATE TABLE dbo.Fornecedores
(
    Id           int IDENTITY(1,1) NOT NULL,
    ClienteId    int NOT NULL,
    Nome         nvarchar(150) NOT NULL,
    Cnpj         nvarchar(18) NULL,
    Email        nvarchar(254) NULL,
    Telefone     nvarchar(30) NULL,
    Endereco     nvarchar(250) NULL,
    Ativo        bit NOT NULL,
    CriadoEmUtc  datetime2 NOT NULL,
    CONSTRAINT PK_Fornecedores PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Fornecedores_Clientes_ClienteId
        FOREIGN KEY (ClienteId) REFERENCES dbo.Clientes(Id) ON DELETE NO ACTION
);
GO

/* 10. Compras */
CREATE TABLE dbo.Compras
(
    Id                 int IDENTITY(1,1) NOT NULL,
    ClienteId          int NOT NULL,
    FornecedorId       int NOT NULL,
    EstabelecimentoId  int NOT NULL,
    NumeroDocumento    nvarchar(80) NULL,
    DataCompraUtc      datetime2 NOT NULL,
    Status             nvarchar(20) NOT NULL,
    Observacao         nvarchar(500) NULL,
    Total              decimal(12,2) NOT NULL,
    CriadoEmUtc        datetime2 NOT NULL,
    RecebidaEmUtc      datetime2 NULL,
    CONSTRAINT PK_Compras PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Compras_Clientes_ClienteId
        FOREIGN KEY (ClienteId) REFERENCES dbo.Clientes(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Compras_Fornecedores_FornecedorId
        FOREIGN KEY (FornecedorId) REFERENCES dbo.Fornecedores(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Compras_Estabelecimentos_EstabelecimentoId
        FOREIGN KEY (EstabelecimentoId) REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION
);
GO

/* 11. Itens de compra */
CREATE TABLE dbo.ItensCompra
(
    Id                  int IDENTITY(1,1) NOT NULL,
    CompraId            int NOT NULL,
    ProdutoId           int NOT NULL,
    NomeProduto         nvarchar(150) NOT NULL,
    CodigoBarrasProduto nvarchar(100) NULL,
    Quantidade          int NOT NULL,
    CustoUnitario       decimal(12,2) NOT NULL,
    TotalItem           decimal(12,2) NOT NULL,
    CONSTRAINT PK_ItensCompra PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ItensCompra_Compras_CompraId
        FOREIGN KEY (CompraId) REFERENCES dbo.Compras(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_ItensCompra_Produtos_ProdutoId
        FOREIGN KEY (ProdutoId) REFERENCES dbo.Produtos(Id) ON DELETE NO ACTION
);
GO

/* 12. Usuários funcionários */
CREATE TABLE dbo.UsuariosFuncionarios
(
    Id              int IDENTITY(1,1) NOT NULL,
    ClienteId       int NOT NULL,
    Nome            nvarchar(150) NOT NULL,
    Email           nvarchar(254) NOT NULL,
    PasswordHash    nvarchar(500) NOT NULL,
    Ativo           bit NOT NULL,
    CriadoEmUtc     datetime2 NOT NULL,
    AtualizadoEmUtc datetime2 NULL,
    CONSTRAINT PK_UsuariosFuncionarios PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_UsuariosFuncionarios_Clientes_ClienteId
        FOREIGN KEY (ClienteId) REFERENCES dbo.Clientes(Id) ON DELETE NO ACTION
);
GO

/* 13. Vínculo de funcionário, estabelecimento e permissões */
CREATE TABLE dbo.UsuariosFuncionariosEstabelecimentos
(
    Id                         int IDENTITY(1,1) NOT NULL,
    UsuarioFuncionarioId       int NOT NULL,
    EstabelecimentoId          int NOT NULL,
    PodeVerProdutos            bit NOT NULL,
    PodeEditarProdutos         bit NOT NULL,
    PodeGerenciarEstoque       bit NOT NULL,
    PodeRegistrarVendas        bit NOT NULL,
    PodeVerRelatorios          bit NOT NULL,
    PodeGerenciarCompras       bit NOT NULL,
    PodeGerenciarFornecedores  bit NOT NULL,
    CONSTRAINT PK_UsuariosFuncionariosEstabelecimentos PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_UsuariosFuncionariosEstabelecimentos_Estabelecimentos_EstabelecimentoId
        FOREIGN KEY (EstabelecimentoId) REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_UsuariosFuncionariosEstabelecimentos_UsuariosFuncionarios_UsuarioFuncionarioId
        FOREIGN KEY (UsuarioFuncionarioId) REFERENCES dbo.UsuariosFuncionarios(Id) ON DELETE CASCADE
);
GO

/* Índices da migration final */
CREATE INDEX IX_Produtos_CategoriaId ON dbo.Produtos(CategoriaId);
CREATE UNIQUE INDEX IX_Produtos_CodigoBarras ON dbo.Produtos(CodigoBarras)
    WHERE CodigoBarras IS NOT NULL;
CREATE INDEX IX_Produtos_EstabelecimentoId ON dbo.Produtos(EstabelecimentoId);
CREATE INDEX IX_Categorias_EstabelecimentoId ON dbo.Categorias(EstabelecimentoId);
CREATE INDEX IX_Produtos_ClienteId ON dbo.Produtos(ClienteId);
CREATE INDEX IX_Categorias_ClienteId ON dbo.Categorias(ClienteId);
CREATE UNIQUE INDEX IX_Estabelecimentos_ClienteId_EhEstoqueCentral
    ON dbo.Estabelecimentos(ClienteId, EhEstoqueCentral)
    WHERE EhEstoqueCentral = 1;

CREATE UNIQUE INDEX IX_ProdutosEstabelecimentos_ProdutoId_EstabelecimentoId
    ON dbo.ProdutosEstabelecimentos(ProdutoId, EstabelecimentoId);
CREATE INDEX IX_ProdutosEstabelecimentos_EstabelecimentoId_Ativo
    ON dbo.ProdutosEstabelecimentos(EstabelecimentoId, Ativo);

CREATE INDEX IX_MovimentacoesEstoque_EstabelecimentoId_CriadoEmUtc
    ON dbo.MovimentacoesEstoque(EstabelecimentoId, CriadoEmUtc);
CREATE INDEX IX_MovimentacoesEstoque_ProdutoId_CriadoEmUtc
    ON dbo.MovimentacoesEstoque(ProdutoId, CriadoEmUtc);

CREATE INDEX IX_Vendas_EstabelecimentoId_DataVendaUtc
    ON dbo.Vendas(EstabelecimentoId, DataVendaUtc);
CREATE UNIQUE INDEX IX_Vendas_EstabelecimentoId_IdVendaExterna
    ON dbo.Vendas(EstabelecimentoId, IdVendaExterna);
CREATE INDEX IX_ItensVenda_ProdutoId ON dbo.ItensVenda(ProdutoId);
CREATE INDEX IX_ItensVenda_VendaId ON dbo.ItensVenda(VendaId);

CREATE INDEX IX_Fornecedores_ClienteId_Nome ON dbo.Fornecedores(ClienteId, Nome);
CREATE UNIQUE INDEX IX_Fornecedores_ClienteId_Cnpj ON dbo.Fornecedores(ClienteId, Cnpj)
    WHERE Cnpj IS NOT NULL;
CREATE INDEX IX_Compras_ClienteId_CriadoEmUtc ON dbo.Compras(ClienteId, CriadoEmUtc);
CREATE INDEX IX_Compras_EstabelecimentoId_Status ON dbo.Compras(EstabelecimentoId, Status);
CREATE INDEX IX_Compras_FornecedorId ON dbo.Compras(FornecedorId);
CREATE INDEX IX_ItensCompra_CompraId ON dbo.ItensCompra(CompraId);
CREATE INDEX IX_ItensCompra_ProdutoId ON dbo.ItensCompra(ProdutoId);

CREATE UNIQUE INDEX IX_UsuariosFuncionarios_ClienteId_Email
    ON dbo.UsuariosFuncionarios(ClienteId, Email);
CREATE INDEX IX_UsuariosFuncionariosEstabelecimentos_EstabelecimentoId
    ON dbo.UsuariosFuncionariosEstabelecimentos(EstabelecimentoId);
CREATE UNIQUE INDEX IX_UsuariosFuncionariosEstabelecimentos_UsuarioFuncionarioId_EstabelecimentoId
    ON dbo.UsuariosFuncionariosEstabelecimentos(UsuarioFuncionarioId, EstabelecimentoId);
GO

/* Histórico de migrations para manter compatibilidade com Entity Framework Core */
CREATE TABLE dbo.__EFMigrationsHistory
(
    MigrationId    nvarchar(150) NOT NULL,
    ProductVersion nvarchar(32) NOT NULL,
    CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY (MigrationId)
);
GO

INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
VALUES
(N'20261018185459_InitialCreate', N'10.0.13'),
(N'20261018201359_AddClientesEstabelecimentos', N'10.0.13'),
(N'20261025120000_AddPasswordHashAndMovimentacoesEstoque', N'10.0.13'),
(N'20261025170000_AddVendasEItensVenda', N'10.0.13'),
(N'20261025190000_AddCentralWarehouseAndStoreInventory', N'10.0.13'),
(N'20261025200000_AddFornecedoresECompras', N'10.0.13'),
(N'20261026003932_CorrigirModeloFornecedoresCompras', N'10.0.13'),
(N'20261026100000_AddUsuariosFuncionariosEPermissoes', N'10.0.13');
GO

/* Conferência final */
SELECT
    DB_NAME() AS BancoCriado,
    (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS QuantidadeTabelas;
GO
