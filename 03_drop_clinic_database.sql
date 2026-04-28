USE master;
GO

IF DB_ID(N'ClinicAdoNet') IS NOT NULL
BEGIN
    ALTER DATABASE ClinicAdoNet SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ClinicAdoNet;
END;
GO

SELECT N'Database ClinicAdoNet was dropped if it existed.' AS Message;
GO
