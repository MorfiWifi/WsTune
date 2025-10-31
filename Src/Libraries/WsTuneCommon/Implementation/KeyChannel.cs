using System.Collections.Concurrent;
using System.Threading.Channels;
using WsTuneCommon.Interfaces;

namespace WsTuneCommon.Implementation;

public class KeyChannel<TKey, TValue> : IKeyChannel<TKey, TValue>
{
    private ConcurrentDictionary<TKey, Channel<TValue>> _channels = new();


    public async Task<TValue?> WaitToReadAsync(TKey key, CancellationToken token = default)
    {
        var couldFindChanel = _channels.TryGetValue(key, out var channel);

        if (couldFindChanel is false || channel is null)
        {
            channel = Channel.CreateUnbounded<TValue>();
            _channels.TryAdd(key, channel);
        }
        
        var chanelIsReady = await channel.Reader.WaitToReadAsync(token);

        if (chanelIsReady && channel.Reader.TryRead(out var value))
            return value;
        
        return default;
    }

    public Task WriteAsync(TKey key, TValue value, bool forceCreate = false)
    {
        var couldFindChanel = _channels.TryGetValue(key, out var channel);

        if (couldFindChanel is false || channel is null && forceCreate)
        {
            channel = Channel.CreateUnbounded<TValue>();
            _channels.TryAdd(key, channel);
        }
        
        channel?.Writer.TryWrite(value);
        
        return Task.CompletedTask;
    }

    public Task<bool> FinalizeChanelAsync(TKey key)
    {
        var couldFindChanel = _channels.TryGetValue(key, out var channel);
        if (couldFindChanel && channel is not null)
        {
            channel.Writer.TryComplete();
        }
        
        var couldRemove = _channels.TryRemove(key, out _);
        return Task.FromResult(couldRemove);
    }
}