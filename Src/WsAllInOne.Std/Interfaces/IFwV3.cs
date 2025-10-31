// namespace WsTuneCommon.Interfaces;

using System.Threading;
using System.Threading.Tasks;

public interface IFwV3
{
    Task SendDataToListener(byte[] data, int length);
    Task SendDataToServer(byte[] data, int length);
    Task RunAsync(CancellationToken cancellationToken);
}