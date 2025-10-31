namespace WsTuneCommon.Interfaces;

/// <summary>
/// simple definition for key base chanel on top of microsoft Chanels
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public interface IKeyChannel<TKey,TValue> where TKey : notnull 
{
    /// <summary>
    /// Wait to read certain Data from chanel With given identity (key)
    /// </summary>
    /// <param name="key">identity of chanel</param>
    /// <param name="token"></param>
    /// <returns>provided model</returns>
    Task<TValue?> WaitToReadAsync(TKey key , CancellationToken token = default);

    /// <summary>
    /// write value to given chanel (key)
    /// </summary>
    /// <param name="key">identity of chanel</param>
    /// <param name="value"></param>
    /// <param name="forceCreate">crate chanel if not found</param>
    /// <returns></returns>
    Task WriteAsync(TKey key, TValue value , bool forceCreate = false);
    
    /// <summary>
    /// dispose chanel 
    /// </summary>
    /// <param name="key">identity of chanel</param>
    /// <returns></returns>
    Task<bool> FinalizeChanelAsync(TKey key);
}