
#region Filters
using System.Threading.Channels;
using System.Xml;

public struct FilterResult
{
    public bool IsOk { get; set; }
    public string FilterName { get; set; }
    public bool OnException { get; set; }

    public override string ToString()
    {
        return $"isOk: {IsOk}, FilterName: {FilterName}, OnException: {OnException}";
    }

    public static FilterResult CreateOnException(string filterName)
    {
        return new FilterResult
        {
            IsOk = false,
            FilterName = filterName,
            OnException = true
        };
    }
}

public interface IFilter
{
    string Name { get; }
    // Input is a stream, meaning we can also use them to discover if file is a json or something else
    Task IsOk(Stream str, ChannelWriter<FilterResult> channel, CancellationToken token);
}

class Filter : IFilter
{
    private IFilter _filter;

    public string Name => _filter.Name;

    private Filter(IFilter filter)
    {
        _filter = filter;
    }
    internal static IFilter WrapWithTryCatch(IFilter filter)
    {
        return new Filter(filter);
    }

    public async Task IsOk(Stream str, ChannelWriter<FilterResult> channel, CancellationToken token)
    {
        try
        {
            await _filter.IsOk(str, channel, token);
        }
        catch
        {
            await channel.WriteAsync(FilterResult.CreateOnException(_filter.Name));
        }
    }
}

// it's ok result if content is a xml with Quick as name
class XmlTypeFilterOneAndQuick : IFilter
{
    public string Name => nameof(XmlTypeFilterOneAndQuick);

    public async Task IsOk(Stream str, ChannelWriter<FilterResult> channel, CancellationToken token)
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
                        await channel.WriteAsync(new FilterResult { IsOk = true, FilterName = Name });
                        // return immediatly after sending a result
                        return;
                    }
                    break;
                default:
                    break;
            }
        }

        // xml is not the type requested
        await channel.WriteAsync(new FilterResult { IsOk = false, FilterName = Name });
    }
}

// it's ok result if content is a xml with Slow as name
class XmlTypeFilterSecondAndSlow : IFilter
{
    public string Name => nameof(XmlTypeFilterSecondAndSlow);
    public async Task IsOk(Stream str, ChannelWriter<FilterResult> channel, CancellationToken token)
    {
        using var xmlReader = XmlReader.Create(str, new XmlReaderSettings { Async = true });

        // stop when cancellation is requested
        while (!token.IsCancellationRequested
        && await xmlReader.ReadAsync())
        {
            // make it slower
            await Task.Delay(100);
            // logic to discover if xml is expected one
            switch (xmlReader.NodeType)
            {
                case XmlNodeType.Text:
                    var val = await xmlReader.GetValueAsync();
                    if (val == "Slow")
                    {
                        // xml is the one expected
                        await channel.WriteAsync(new FilterResult { IsOk = true, FilterName = Name });
                        // return immediatly after sending a result
                        return;
                    }
                    break;
                default:
                    break;
            }
        }

        // xml is not the type requested
        await channel.WriteAsync(new FilterResult { IsOk = false, FilterName = Name });
    }
}

#endregion