-- MERKDITO: leitura apenas, NAO cria, apaga nem modifica dados.
-- Execute no SSMS conectado ao SQL Server da equipe.
SET NOCOUNT ON;
SELECT @@SERVERNAME AS ServidorSQL,
       DB_ID(N'MerkditoDb') AS IdBancoMerkdito,
       CASE WHEN DB_ID(N'MerkditoDb') IS NULL THEN N'BANCO AUSENTE'
            ELSE N'BANCO EXISTE - NAO EXECUTAR O SCRIPT 00 SEM CONFERIR' END AS Estado;
IF DB_ID(N'MerkditoDb') IS NOT NULL
BEGIN
  EXEC(N'USE [MerkditoDb];
    SELECT t.name AS Tabela, s.name AS Esquema
    FROM sys.tables AS t JOIN sys.schemas s ON s.schema_id=t.schema_id
    WHERE t.name IN
      (N''Clientes'',N''Estabelecimentos'',N''Produtos'',N''ProdutosEstabelecimentos'',
       N''Vendas'',N''ItensVenda'',N''Consumidores'',N''ConsumidorEstabelecimentos'',
       N''PedidosConsumidores'',N''__EFMigrationsHistory'')
    ORDER BY t.name;');
END;
