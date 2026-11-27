/*
  MERKDITO / APP CONSUMIDOR — ALTERAÇÃO ADITIVA PARA HOMOLOGAÇÃO
  Conferir backup e versão do banco. Aplicar depois do script 00 original.
  Não aplicar em produção sem autorização e validação de rollback.
*/
USE [MerkditoDb];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Consumidores', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.PedidosConsumidores', N'U') IS NOT NULL
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51001, 'Tabelas do consumidor já existem. Verifique a versão antes de reaplicar o script.', 1;
END;
CREATE TABLE dbo.Consumidores (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Consumidores PRIMARY KEY,
  Nome NVARCHAR(150) NOT NULL,
  Email NVARCHAR(254) NOT NULL,
  PasswordHash NVARCHAR(500) NOT NULL,
  EstabelecimentoId INT NOT NULL,
  Ativo BIT NOT NULL CONSTRAINT DF_Consumidores_Ativo DEFAULT (1),
  CriadoEmUtc DATETIME2 NOT NULL CONSTRAINT DF_Consumidores_Criado DEFAULT (SYSUTCDATETIME()),
  CONSTRAINT FK_Consumidores_Estabelecimentos FOREIGN KEY (EstabelecimentoId)
    REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION
);
CREATE INDEX IX_Consumidores_EstabelecimentoId ON dbo.Consumidores(EstabelecimentoId);
CREATE UNIQUE INDEX IX_Consumidores_Email ON dbo.Consumidores(Email);
CREATE TABLE dbo.PedidosConsumidores (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidosConsumidores PRIMARY KEY,
  ConsumidorId INT NOT NULL,
  VendaId INT NOT NULL,
  EstabelecimentoId INT NOT NULL,
  ChaveIdempotencia NVARCHAR(64) NOT NULL,
  ResumoItensSha256 NVARCHAR(64) NOT NULL,
  CriadoEmUtc DATETIME2 NOT NULL CONSTRAINT DF_PedidosConsumidores_Criado DEFAULT (SYSUTCDATETIME()),
  CONSTRAINT FK_PedidosConsumidores_Consumidores FOREIGN KEY (ConsumidorId)
    REFERENCES dbo.Consumidores(Id) ON DELETE NO ACTION,
  CONSTRAINT FK_PedidosConsumidores_Vendas FOREIGN KEY (VendaId)
    REFERENCES dbo.Vendas(Id) ON DELETE NO ACTION,
  CONSTRAINT FK_PedidosConsumidores_Estabelecimentos FOREIGN KEY (EstabelecimentoId)
    REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION
);
CREATE INDEX IX_PedidosConsumidores_EstabelecimentoId ON dbo.PedidosConsumidores(EstabelecimentoId);
CREATE UNIQUE INDEX IX_PedidosConsumidores_ConsumidorId_ChaveIdempotencia ON dbo.PedidosConsumidores(ConsumidorId,ChaveIdempotencia);
CREATE UNIQUE INDEX IX_PedidosConsumidores_VendaId ON dbo.PedidosConsumidores(VendaId);
-- Mantém a linha de histórico alinhada com a migration 20261109000000.
IF NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261109000000_AddConsumidorApp')
 INSERT INTO dbo.__EFMigrationsHistory(MigrationId, ProductVersion) VALUES(N'20261109000000_AddConsumidorApp',N'10.0.13');
COMMIT TRANSACTION;
GO
