-- The tables PipelineBuilder keeps in its database. Safe to run again: only what is missing is made.
-- Run by the install script; the app itself only reads and writes rows.

IF OBJECT_ID(N'dbo.SavedPipelines', N'U') IS NULL
    CREATE TABLE dbo.SavedPipelines
    (
        Id       int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_SavedPipelines PRIMARY KEY,
        Name     nvarchar(200)      NOT NULL CONSTRAINT UQ_SavedPipelines_Name UNIQUE,
        Settings nvarchar(max)      NOT NULL, -- the same JSON as a saved settings file
        SavedBy  nvarchar(256)      NOT NULL,
        SavedAt  datetimeoffset(0)  NOT NULL
    );

IF OBJECT_ID(N'dbo.ActivityLog', N'U') IS NULL
    CREATE TABLE dbo.ActivityLog
    (
        Id       bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ActivityLog PRIMARY KEY,
        At       datetimeoffset(0)     NOT NULL,
        UserName nvarchar(256)         NOT NULL,
        Action   nvarchar(50)          NOT NULL, -- Saved, Opened, Deleted, Downloaded, Copied
        Pipeline nvarchar(200)         NOT NULL
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityLog_At' AND object_id = OBJECT_ID(N'dbo.ActivityLog'))
    CREATE INDEX IX_ActivityLog_At ON dbo.ActivityLog (At DESC);
