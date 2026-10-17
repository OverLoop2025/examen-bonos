USE BonusTest;
GO
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRANSACTION;

CREATE TABLE dbo.Bonus (
    BonusId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bonus PRIMARY KEY,
    DepartmentCode CHAR(5)           NOT NULL,
    EmployeeNumber INT               NOT NULL,
    Period         CHAR(7)           NOT NULL,
    BonusTypeId    INT               NOT NULL,
    [Value]        DECIMAL(28,14)    NOT NULL,
    ReasonCode     CHAR(6)           NOT NULL,
    MonthlySalary  DECIMAL(16,2)     NOT NULL,
    TaxRate        DECIMAL(5,2)      NOT NULL,
    GrossAmount    DECIMAL(16,2)     NOT NULL,
    TaxAmount      DECIMAL(16,2)     NOT NULL,
    NetAmount      DECIMAL(16,2)     NOT NULL,
    StateCode      CHAR(4)           NOT NULL CONSTRAINT DF_Bonus_StateCode DEFAULT '0201',
    IsDeleted      BIT               NOT NULL CONSTRAINT DF_Bonus_IsDeleted DEFAULT 0,
    CreatedBy      NVARCHAR(50)      NOT NULL,
    CreatedAt      DATETIMEOFFSET(7) NOT NULL,
    ModifiedBy     NVARCHAR(50)      NULL,
    ModifiedAt     DATETIMEOFFSET(7) NULL,
    RowVersion     ROWVERSION       NOT NULL,

    CONSTRAINT FK_Bonus_Employee FOREIGN KEY (DepartmentCode, EmployeeNumber)
        REFERENCES dbo.Employee (DepartmentCode, EmployeeNumber),
    CONSTRAINT FK_Bonus_Type FOREIGN KEY (BonusTypeId) REFERENCES dbo.BonusType (BonusTypeId),
    CONSTRAINT FK_Bonus_Reason FOREIGN KEY (ReasonCode) REFERENCES dbo.BonusReason (ReasonCode),
    CONSTRAINT FK_Bonus_State FOREIGN KEY (StateCode) REFERENCES dbo.State (StateCode),
    CONSTRAINT CK_Bonus_Period CHECK (
        Period COLLATE Latin1_General_100_BIN2 LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]'
        AND LEFT(Period, 4) >= '0001' AND RIGHT(Period, 2) BETWEEN '01' AND '12'),
    CONSTRAINT CK_Bonus_Type CHECK (BonusTypeId IN (1, 2)),
    CONSTRAINT CK_Bonus_State CHECK (StateCode IN ('0201', '0202', '0203')),
    CONSTRAINT CK_Bonus_EmployeeAmounts CHECK (MonthlySalary > 0 AND TaxRate BETWEEN 0 AND 100),
    CONSTRAINT CK_Bonus_Value CHECK (
        [Value] > 0 AND ((BonusTypeId = 1 AND [Value] <= MonthlySalary) OR (BonusTypeId = 2 AND [Value] <= 100))),
    CONSTRAINT CK_Bonus_Amounts CHECK (
        GrossAmount >= 0 AND GrossAmount <= MonthlySalary
        AND GrossAmount = CASE WHEN BonusTypeId = 1 THEN ROUND([Value], 2)
            ELSE ROUND(MonthlySalary * CAST(CASE WHEN BonusTypeId = 2 AND [Value] BETWEEN 0 AND 100
                THEN [Value] ELSE 0 END AS DECIMAL(17,14)) / 100, 2) END
        AND TaxAmount = ROUND(GrossAmount * TaxRate / 100, 2)
        AND NetAmount = GrossAmount - TaxAmount),
    CONSTRAINT CK_Bonus_Deletion CHECK (IsDeleted = 0 OR StateCode = '0201'),
    CONSTRAINT CK_Bonus_Audit CHECK (
        LEN(LTRIM(RTRIM(CreatedBy))) > 0
        AND ((ModifiedBy IS NULL AND ModifiedAt IS NULL AND StateCode = '0201' AND IsDeleted = 0)
            OR (ModifiedBy IS NOT NULL AND LEN(LTRIM(RTRIM(ModifiedBy))) > 0
                AND ModifiedAt IS NOT NULL AND ModifiedAt >= CreatedAt)))
);

-- Incluye pagados y protege también dos registros simultáneos del mismo periodo.
CREATE UNIQUE INDEX UX_Bonus_Employee_Period
    ON dbo.Bonus (DepartmentCode, EmployeeNumber, Period)
    WHERE StateCode <> '0203' AND IsDeleted = 0;

COMMIT;
GO
