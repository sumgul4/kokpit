/*
  CAE Kokpit – reporting views (column CONTRACT).
  The data team fills each SELECT from the real source tables. Column names and types must stay as below:
  KokpitDataService maps them by name to the classes in KokpitModels.cs.

  Code values:
    Stage          : 'Not Started' | 'Fieldwork' | 'Reporting' | 'Issued'
    Risk           : 'High' | 'Medium' | 'Low'
    Status         : 'Open' | 'InVerification' | 'Closed'
    MovementType   : 'Joined' | 'InternalTransfer' | 'Left'
    PromotionType  : 'Full' | 'Half'
    StepCode       : '1'..'6', 'A', 'B', '7'..'12'
*/

CREATE SCHEMA cockpit;
GO

-- Enable change tracking so the app can cheaply detect new data:
-- ALTER DATABASE <ReportingDb> SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);
-- ALTER TABLE <each source table> ENABLE CHANGE_TRACKING;

CREATE OR ALTER VIEW cockpit.vw_Audit AS
SELECT
    CAST(NULL AS int)           AS AuditId,
    CAST(NULL AS nvarchar(200)) AS AuditName,
    CAST(NULL AS nvarchar(100)) AS Area,             -- Şube Denetimi / Genel Müdürlük / BT Denetimi / Süreç & Uyum / İştirakler
    CAST(NULL AS nvarchar(20))  AS Stage,
    CAST(NULL AS datetime2)     AS PlannedStart,
    CAST(NULL AS datetime2)     AS PlannedEnd,
    CAST(NULL AS datetime2)     AS PlannedIssueDate, -- planned report issue date (NULL → PlannedEnd + CycleTimeTargetDays)
    CAST(NULL AS datetime2)     AS ActualEnd,
    CAST(NULL AS bit)           AS IsDelayed         -- business rule agreed in the design doc
WHERE 1 = 0;
GO

CREATE OR ALTER VIEW cockpit.vw_Report AS
SELECT
    CAST(NULL AS nvarchar(30))  AS ReportId,
    CAST(NULL AS int)           AS AuditId,
    CAST(NULL AS nvarchar(200)) AS AuditName,
    CAST(NULL AS nvarchar(100)) AS Area,
    CAST(NULL AS nvarchar(20))  AS AuditStage,
    CAST(NULL AS nvarchar(5))   AS CurrentStepCode,      -- NULL when issued
    CAST(NULL AS datetime2)     AS CurrentStepEnteredAt,
    CAST(NULL AS datetime2)     AS IssuedAt,
    CAST(NULL AS int)           AS CycleDays,
    CAST(NULL AS bit)           AS HasSkippedSteps
WHERE 1 = 0;
GO

CREATE OR ALTER VIEW cockpit.vw_ReportStep AS
SELECT
    CAST(NULL AS nvarchar(30))  AS ReportId,
    CAST(NULL AS nvarchar(5))   AS StepCode,
    CAST(NULL AS datetime2)     AS EnteredAt,
    CAST(NULL AS datetime2)     AS ExitedAt,
    CAST(NULL AS decimal(9,2))  AS WaitDays              -- NULL while the step is open
WHERE 1 = 0;
GO

CREATE OR ALTER VIEW cockpit.vw_StepSla AS
SELECT * FROM (VALUES
    ('1',  N'İlk okumaya gönderildi',               3,  1),
    ('2',  N'İlk gözden geçirme yapıldı',           5,  2),
    ('3',  N'İkinci okumaya gönderildi',            3,  3),
    ('4',  N'İkinci gözden geçirme yapıldı',        5,  4),
    ('5',  N'Şubeye gönderildi',                    2,  5),
    ('6',  N'Cevaplar şubeden alındı',              15, 6),
    ('A',  N'Şablon kontrolüne gönderildi',         2,  7),
    ('B',  N'Şablon kontrolü yapıldı',              3,  8),
    ('7',  N'Son okumaya gönderildi',               3,  9),
    ('8',  N'Son gözden geçirme yapıldı',           5,  10),
    ('9',  N'Başkanlığa gönderildi',                2,  11),
    ('10', N'Yönetim gözden geçirmesi tamamlandı',  7,  12),
    ('11', N'Rapor sevk edildi',                    2,  13),
    ('12', N'Rapor dağıtımı yapıldı',               2,  14)
) AS s (StepCode, StepName, SlaDays, SortOrder);   -- SLA values are placeholders until the board confirms them
GO

CREATE OR ALTER VIEW cockpit.vw_Finding AS
SELECT
    CAST(NULL AS int)           AS FindingId,
    CAST(NULL AS int)           AS AuditId,
    CAST(NULL AS nvarchar(100)) AS Area,
    CAST(NULL AS nvarchar(10))  AS Risk,
    CAST(NULL AS nvarchar(100)) AS ActionOwner,       -- from ONE master unit list
    CAST(NULL AS datetime2)     AS ReportDate,
    CAST(NULL AS datetime2)     AS DueDate,
    CAST(NULL AS datetime2)     AS ClosedDate,
    CAST(NULL AS nvarchar(20))  AS Status,
    CAST(NULL AS bit)           AS IsRepeat
WHERE 1 = 0;
GO

CREATE OR ALTER VIEW cockpit.vw_Staff AS
SELECT
    CAST(NULL AS nvarchar(20))  AS EmployeeId,
    CAST(NULL AS nvarchar(100)) AS Title,
    CAST(NULL AS nvarchar(100)) AS Area,
    CAST(NULL AS datetime2)     AS HireDate,
    CAST(NULL AS decimal(7,1))  AS TrainingHoursYtd,
    CAST(NULL AS nvarchar(200)) AS Certificates       -- comma separated: 'CIA,CISA'
WHERE 1 = 0;
GO

CREATE OR ALTER VIEW cockpit.vw_StaffMovement AS
SELECT
    CAST(NULL AS nvarchar(20))  AS EmployeeId,
    CAST(NULL AS datetime2)     AS MovementDate,
    CAST(NULL AS nvarchar(20))  AS MovementType,
    CAST(NULL AS nvarchar(100)) AS MovementDetail,    -- source (exam/transfer) or target (unit/resignation/retirement)
    CAST(NULL AS nvarchar(100)) AS Title,
    CAST(NULL AS nvarchar(100)) AS Area
WHERE 1 = 0;
GO

CREATE OR ALTER VIEW cockpit.vw_Promotion AS
SELECT
    CAST(NULL AS nvarchar(20))  AS EmployeeId,
    CAST(NULL AS int)           AS PeriodYear,
    CAST(NULL AS nvarchar(100)) AS FromTitle,
    CAST(NULL AS nvarchar(100)) AS ToTitle,
    CAST(NULL AS decimal(5,2))  AS DevelopmentScore,  -- gelişim sepeti puanı
    CAST(NULL AS nvarchar(10))  AS PromotionType,
    CAST(NULL AS nvarchar(100)) AS Area
WHERE 1 = 0;
GO

CREATE OR ALTER VIEW cockpit.vw_CapacityMonthly AS
SELECT
    CAST(NULL AS nvarchar(100)) AS Area,
    CAST(NULL AS datetime2)     AS Month,             -- first day of the month
    CAST(NULL AS decimal(9,1))  AS PlannedManDays,
    CAST(NULL AS decimal(9,1))  AS ActualManDays,
    CAST(NULL AS decimal(4,2))  AS SurveyScore,
    CAST(NULL AS int)           AS SurveyCount
WHERE 1 = 0;
GO
