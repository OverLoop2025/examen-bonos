using BonusTest.Domain.Entities;
using BonusTest.Application.Common;

namespace BonusTest.Application.Bonuses;

public interface IBonusRepository
{
    Task<PagedResponse<BonusDto>> ListAsync(BonusFilter filter, CancellationToken cancellationToken);
    Task<Employee?> GetEmployeeAsync(string departmentCode, int employeeNumber, CancellationToken cancellationToken);
    Task<Bonus> RegisterAsync(RegisterBonusRequest request, string userCode, DateTimeOffset now, CancellationToken cancellationToken);
    Task<Bonus?> FindAsync(int bonusId, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task PayAsync(int bonusId, string userCode, DateTimeOffset now, CancellationToken cancellationToken);
}
