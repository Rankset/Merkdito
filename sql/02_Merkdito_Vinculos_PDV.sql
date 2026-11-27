/* Aplicar SOMENTE em banco de teste, APÓS 00 e 01. Não usar junto com EF Database.Migrate(). */
USE [MerkditoDb];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ConsumidorEstabelecimentos', N'U') IS NOT NULL
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51002, 'Vinculos ja existem; nao reaplique este script.', 1;
END;
CREATE TABLE dbo.ConsumidorEstabelecimentos (
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConsumidorEstabelecimentos PRIMARY KEY,
    ConsumidorId INT NOT NULL,
    EstabelecimentoId INT NOT NULL,
    CriadoEmUtc DATETIME2 NOT NULL CONSTRAINT DF_ConsumidorEstabelecimentos_Criado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_ConsumidorEstabelecimentos_Consumidores FOREIGN KEY(ConsumidorId) REFERENCES dbo.Consumidores(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ConsumidorEstabelecimentos_Estabelecimentos FOREIGN KEY(EstabelecimentoId) REFERENCES dbo.Estabelecimentos(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX IX_ConsumidorEstabelecimentos_ConsumidorId_EstabelecimentoId ON dbo.ConsumidorEstabelecimentos(ConsumidorId, EstabelecimentoId);
CREATE INDEX IX_ConsumidorEstabelecimentos_EstabelecimentoId ON dbo.ConsumidorEstabelecimentos(EstabelecimentoId);
INSERT INTO dbo.ConsumidorEstabelecimentos(ConsumidorId,EstabelecimentoId)
 SELECT Id,EstabelecimentoId FROM dbo.Consumidores;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261112000000_AddVinculosConsumidorLoja')
 INSERT INTO dbo.__EFMigrationsHistory(MigrationId,ProductVersion) VALUES(N'20261112000000_AddVinculosConsumidorLoja',N'10.0.13');
COMMIT TRANSACTION;
GO
