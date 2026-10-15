namespace Vcontrol.Worker;

internal interface IVclientService
{
    Task<VclientQueryResult> QueryAsync(IEnumerable<string> commands, string source, CancellationToken ct);
}