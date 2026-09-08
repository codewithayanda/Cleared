namespace Cleared.Application.Abstractions;

public interface ICreditNoteNumberAllocator
{
    Task<string> AllocateAsync(Guid tenantId, int year, CancellationToken cancellationToken);
}
