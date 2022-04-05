
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Xml;

// this is the body content that we wants to discover
var messageContentForQuickFilter = Encoding.UTF8.GetBytes(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no"" ?>
<person>
    <name>Quick</name>
</person>");
// use this one to trigger success from second filter
var messageContentForSlowFilter = Encoding.UTF8.GetBytes(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no"" ?>
<person>
    <name>Slow</name>
</person>");
// Update me depending what scenario you want to test
var messageToUse = messageContentForQuickFilter;
// var messageToUse = messageContentForSlowFilter;

// channel is acting like a publish / sub. Filters will send messages of type FilterResult, and main process will read them.
var channel = Channel.CreateUnbounded<FilterResult>();
var cancellationTokenSource = new CancellationTokenSource();

var filters = new IFilter[] {
        new XmlTypeFilterOne(),
        new XmlTypeFilterSecondAndSlow()
    }
    .Select(f => Filter.WrapWithTryCatch(f))
    .ToArray();

// start stop watch AFTER filters array creation. All calls are independant, meaning that they can be registered as singleton 
var stopWatch = new Stopwatch();
// Run all filters in parallel.
stopWatch.Start();
var allTasksAsSingleOne = Parallel.ForEach(
    filters,
    async f =>
    {
        await f.IsOk(new MemoryStream(messageToUse), channel.Writer, cancellationTokenSource.Token);
    }
);

FilterResult endResult = default;

// Processing time will take between quickest filter and longest filter.
for (var i = 0; i < filters.Length || allTasksAsSingleOne.IsCompleted; i++)
{
    // read all process one by one
    var result = await channel.Reader.ReadAsync();
    if (result.IsOk)
    {
        // stop all remainings tasks
        cancellationTokenSource.Cancel();
        endResult = result;
        break;
    }
}
stopWatch.Stop();

Console.WriteLine(endResult.ToString());
Console.WriteLine($"elapsed time: {stopWatch.ElapsedMilliseconds}");


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

#region Filters
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
class XmlTypeFilterOne : IFilter
{
    public string Name => nameof(XmlTypeFilterOne);

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