USE BonusTest;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.BonusAllocation (
    BonusId     INT            NOT NULL,
    LineNumber  INT            NOT NULL,
    ProjectName VARCHAR(100)   NOT NULL,
    Hours       DECIMAL(18,2)  NOT NULL,
    Amount      DECIMAL(16,2)  NOT NULL,
    CONSTRAINT PK_BonusAllocation PRIMARY KEY (BonusId, LineNumber),
    CONSTRAINT FK_BonusAllocation_Bonus FOREIGN KEY (BonusId) REFERENCES dbo.Bonus (BonusId),
    CONSTRAINT CK_BonusAllocation_Line CHECK (LineNumber > 0),
    CONSTRAINT CK_BonusAllocation_Project CHECK (LEN(LTRIM(RTRIM(ProjectName))) > 0),
    CONSTRAINT CK_BonusAllocation_Hours CHECK (Hours > 0),
    CONSTRAINT CK_BonusAllocation_Amount CHECK (Amount >= 0)
);

COMMIT;
GO
