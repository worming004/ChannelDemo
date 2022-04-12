
using System.Threading.Channels;
using System.Xml;

public struct StrategyResult
{
    public bool IsOk { get; set; }
    public string StrategyName { get; set; }
    public bool OnException { get; set; }

    public override string ToString()
    {
        return $"isOk: {IsOk}, StrategyName: {StrategyName}, OnException: {OnException}";
    }

    public static StrategyResult CreateOnException(string strategyName)
    {
        return new StrategyResult
        {
            IsOk = false,
            StrategyName = strategyName,
            OnException = true
        };
    }
}

public interface IStrategy
{
    string Name { get; }
    // Input is a stream, meaning we can also use them to discover if file is a json or something else
    Task IsMatch(Stream str, ChannelWriter<StrategyResult> channel, CancellationToken token);
}

class StrategyWrapper : IStrategy
{
    private IStrategy _strategy;

    public string Name => _strategy.Name;

    private StrategyWrapper(IStrategy strategy)
    {
        _strategy = strategy;
    }

    internal static IStrategy WrapWithTryCatch(IStrategy strategy)
    {
        return new StrategyWrapper(strategy);
    }

    public async Task IsMatch(Stream str, ChannelWriter<StrategyResult> channel, CancellationToken token)
    {
        try
        {
            await _strategy.IsMatch(str, channel, token);
        }
        catch
        {
            await channel.WriteAsync(StrategyResult.CreateOnException(_strategy.Name));
        }
    }
}

// it's ok result if content is a xml with Quick as name
class XmlTypeStrategyOneAndQuick : IStrategy
{
    public string Name => nameof(XmlTypeStrategyOneAndQuick);

    public async Task IsMatch(Stream str, ChannelWriter<StrategyResult> channel, CancellationToken token)
    {
        using var xmlReader = XmlReader.Create(str, new XmlReaderSettings { Async = true });

        // stop when cancellation is requested
        while (!token.IsCancellationRequested
        && await xmlReader.ReadAsync())
        {
            // logic to discover if xml is expected one
            switch (xmlReader.NodeType)
            {
                case XmlNodeType.Text:
                    var val = await xmlReader.GetValueAsync();
                    if (val == "Quick")
                    {
                        // xml is the one expected
                        await channel.WriteAsync(new StrategyResult { IsOk = true, StrategyName = Name });
                        // return immediatly after sending a result
                        return;
                    }
                    break;
                default:
                    break;
            }
        }

        // xml is not the type requested
        await channel.WriteAsync(new StrategyResult { IsOk = false, StrategyName = Name });
    }
}

// it's ok result if content is a xml with Slow as name
class XmlTypeStrategySecondAndSlow : IStrategy
{
    public string Name => nameof(XmlTypeStrategySecondAndSlow);
    public async Task IsMatch(Stream str, ChannelWriter<StrategyResult> channel, CancellationToken token)
    {
        using var xmlReader = XmlReader.Create(str, new XmlReaderSettings { Async = true });

        // stop when cancellation is requested
        while (!token.IsCancellationRequested
        && await xmlReader.ReadAsync())
        {
            // make it slower
            await Task.Delay(10);
            // logic to discover if xml is expected one
            switch (xmlReader.NodeType)
            {
                case XmlNodeType.Text:
                    var val = await xmlReader.GetValueAsync();
                    if (val == "Slow")
                    {
                        // xml is the one expected
                        await channel.WriteAsync(new StrategyResult { IsOk = true, StrategyName = Name });
                        // return immediatly after sending a result
                        return;
                    }
                    break;
                default:
                    break;
            }
        }

        // xml is not the type requested
        await channel.WriteAsync(new StrategyResult { IsOk = false, StrategyName = Name });
    }
}
