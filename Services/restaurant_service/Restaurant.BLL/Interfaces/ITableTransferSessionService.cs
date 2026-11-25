using Shared.Common.TableTransfers;

namespace Restaurant.BLL.Interfaces;

public interface ITableTransferSessionService
{
    Task<TableTransferResultDTO> Prepare(TableTransferDTO request, CancellationToken ct);
    Task<TableTransferResultDTO> Finish(Guid id, bool abort, CancellationToken ct);
    Task<TableTransferResultDTO> GetById(Guid id, CancellationToken ct);
}
