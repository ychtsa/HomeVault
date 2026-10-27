-- Run in the HomeVault database as the configured Microsoft Entra SQL administrator.
-- Replace both IDs with runtimePrincipalId/deployPrincipalId from the Bicep outputs.
-- Using explicit SIDs avoids granting the SQL server Microsoft Graph directory permissions.
DECLARE @runtimeObjectId uniqueidentifier = '00000000-0000-0000-0000-000000000000';
DECLARE @deployObjectId uniqueidentifier = '00000000-0000-0000-0000-000000000000';
IF @runtimeObjectId = '00000000-0000-0000-0000-000000000000'
    OR @deployObjectId = '00000000-0000-0000-0000-000000000000'
    THROW 50000, 'Replace the two managed identity object IDs before running this script.', 1;

DECLARE @sql nvarchar(max);
IF DATABASE_PRINCIPAL_ID('homevault-runtime') IS NULL
BEGIN
    SET @sql = N'CREATE USER [homevault-runtime] WITH SID = '
        + CONVERT(varchar(34), CONVERT(varbinary(16), @runtimeObjectId), 1) + N', TYPE = E';
    EXEC sp_executesql @sql;
END;
IF DATABASE_PRINCIPAL_ID('homevault-deploy') IS NULL
BEGIN
    SET @sql = N'CREATE USER [homevault-deploy] WITH SID = '
        + CONVERT(varchar(34), CONVERT(varbinary(16), @deployObjectId), 1) + N', TYPE = E';
    EXEC sp_executesql @sql;
END;
ALTER ROLE db_datareader ADD MEMBER [homevault-runtime];
ALTER ROLE db_datawriter ADD MEMBER [homevault-runtime];
ALTER ROLE db_datareader ADD MEMBER [homevault-deploy];
ALTER ROLE db_datawriter ADD MEMBER [homevault-deploy];
ALTER ROLE db_ddladmin ADD MEMBER [homevault-deploy];
