namespace WsTuneCommon.Interfaces;

public interface IFwV3
{
    Task SendDataToListener( byte[] data, int length);
    Task SendDataToServer( byte[] data, int length);
    Task RunAsync(CancellationToken cancellationToken);
}


public interface IFwV4
{
    string Name { get; }
    
    
    Task SendDataToListenerAsync(Guid connectionId , byte[] data, int length , CancellationToken cancellationToken);
    Task SendDataToServerAsync(Guid connectionId , byte[] data, int length , CancellationToken cancellationToken);
    Task OpenServerConnectionAsync(Guid connectionId ,CancellationToken cancellationToken);
    
    
    
    Task DisposeClientAsync(Guid id);
    Task RunAsync(CancellationToken cancellationToken);
}