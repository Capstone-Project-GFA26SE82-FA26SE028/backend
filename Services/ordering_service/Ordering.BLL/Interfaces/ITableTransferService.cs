using Shared.Common.TableTransfers;

namespace Ordering.BLL.Interfaces;

public interface ITableTransferService
{
    Task<TableTransferResultDTO> Execute(TableTransferDTO request, CancellationToken ct);
    Task<TableTransferResultDTO> GetById(Guid id, CancellationToken ct);
}

public interface ITableTransferSessionClient
{
    Task<bool> CanTransferAsync(Guid source, Guid target, CancellationToken ct);
    Task<TableTransferResultDTO> PrepareAsync(TableTransferDTO request, CancellationToken ct);
    Task<TableTransferResultDTO> FinishAsync(Guid id, bool abort, CancellationToken ct);
    Task<TableTransferResultDTO> GetAsync(Guid id, CancellationToken ct);
}
