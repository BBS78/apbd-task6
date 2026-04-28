IF DB_ID(N'ClinicAdoNet') IS NULL
BEGIN
    CREATE DATABASE ClinicAdoNet;
END;
GO

USE ClinicAdoNet;
GO

DROP TABLE IF EXISTS dbo.Appointments;
DROP TABLE IF EXISTS dbo.Doctors;
DROP TABLE IF EXISTS dbo.Patients;
DROP TABLE IF EXISTS dbo.Specializations;
GO

CREATE TABLE dbo.Specializations
(
    IdSpecialization INT IDENTITY(1,1) CONSTRAINT PK_Specializations PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_Specializations_Name UNIQUE (Name)
);
GO

CREATE TABLE dbo.Patients
(
    IdPatient INT IDENTITY(1,1) CONSTRAINT PK_Patients PRIMARY KEY,
    FirstName NVARCHAR(80) NOT NULL,
    LastName NVARCHAR(80) NOT NULL,
    Email NVARCHAR(120) NOT NULL,
    PhoneNumber NVARCHAR(30) NOT NULL,
    DateOfBirth DATE NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Patients_IsActive DEFAULT 1,
    CONSTRAINT UQ_Patients_Email UNIQUE (Email)
);
GO

CREATE TABLE dbo.Doctors
(
    IdDoctor INT IDENTITY(1,1) CONSTRAINT PK_Doctors PRIMARY KEY,
    IdSpecialization INT NOT NULL,
    FirstName NVARCHAR(80) NOT NULL,
    LastName NVARCHAR(80) NOT NULL,
    LicenseNumber NVARCHAR(40) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Doctors_IsActive DEFAULT 1,
    CONSTRAINT FK_Doctors_Specializations FOREIGN KEY (IdSpecialization)
        REFERENCES dbo.Specializations(IdSpecialization),
    CONSTRAINT UQ_Doctors_LicenseNumber UNIQUE (LicenseNumber)
);
GO

CREATE TABLE dbo.Appointments
(
    IdAppointment INT IDENTITY(1,1) CONSTRAINT PK_Appointments PRIMARY KEY,
    IdPatient INT NOT NULL,
    IdDoctor INT NOT NULL,
    AppointmentDate DATETIME2(0) NOT NULL,
    Status NVARCHAR(30) NOT NULL,
    Reason NVARCHAR(250) NOT NULL,
    InternalNotes NVARCHAR(500) NULL,
    CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_Appointments_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Appointments_Patients FOREIGN KEY (IdPatient)
        REFERENCES dbo.Patients(IdPatient),
    CONSTRAINT FK_Appointments_Doctors FOREIGN KEY (IdDoctor)
        REFERENCES dbo.Doctors(IdDoctor),
    CONSTRAINT CK_Appointments_Status CHECK (Status IN (N'Scheduled', N'Completed', N'Cancelled'))
);
GO

INSERT INTO dbo.Specializations (Name)
VALUES
    (N'Cardiology'),
    (N'Dermatology'),
    (N'Pediatrics');
GO

INSERT INTO dbo.Patients (FirstName, LastName, Email, PhoneNumber, DateOfBirth, IsActive)
VALUES
    (N'Anna', N'Kowalska', N'anna.kowalska@example.com', N'+48 500 100 100', '1995-04-12', 1),
    (N'Jan', N'Nowak', N'jan.nowak@example.com', N'+48 500 200 200', '1988-11-03', 1),
    (N'Maria', N'Zielinska', N'maria.zielinska@example.com', N'+48 500 300 300', '2016-06-21', 1),
    (N'Piotr', N'Wisniewski', N'piotr.wisniewski@example.com', N'+48 500 400 400', '1979-02-15', 0);
GO

INSERT INTO dbo.Doctors (IdSpecialization, FirstName, LastName, LicenseNumber, IsActive)
VALUES
    (1, N'Ewa', N'Mazur', N'CARD-1001', 1),
    (2, N'Tomasz', N'Lewandowski', N'DERM-2001', 1),
    (3, N'Karolina', N'Wojcik', N'PED-3001', 1),
    (1, N'Adam', N'Kaminski', N'CARD-1002', 0);
GO

INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason, InternalNotes)
VALUES
    (1, 1, DATEADD(DAY, 2, SYSUTCDATETIME()), N'Scheduled', N'Blood pressure consultation', NULL),
    (2, 2, DATEADD(DAY, 3, SYSUTCDATETIME()), N'Scheduled', N'Skin rash consultation', NULL),
    (3, 3, DATEADD(DAY, 4, SYSUTCDATETIME()), N'Scheduled', N'Child check-up', NULL),
    (1, 1, DATEADD(DAY, -10, SYSUTCDATETIME()), N'Completed', N'ECG control visit', N'Patient stable');
GO

SELECT N'Database ClinicAdoNet was created and seeded.' AS Message;
GO
