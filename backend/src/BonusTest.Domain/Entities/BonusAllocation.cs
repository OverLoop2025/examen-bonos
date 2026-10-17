namespace BonusTest.Domain.Entities;

public class BonusAllocation
{
    private BonusAllocation() { }

    internal BonusAllocation(int lineNumber, string projectName, decimal hours, decimal amount)
    {
        LineNumber = lineNumber;
        ProjectName = projectName;
        Hours = hours;
        Amount = amount;
    }

    public int BonusId { get; private set; }
    public int LineNumber { get; private set; }
    public string ProjectName { get; private set; } = null!;
    public decimal Hours { get; private set; }
    public decimal Amount { get; private set; }
}
