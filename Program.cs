
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

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
// var messageToUse = messageContentForQuickFilter;
var messageToUse = messageContentForSlowFilter;

// channel is acting like a publish / sub. Filters will send messages of type FilterResult, and main process will read them.
var channel = Channel.CreateUnbounded<FilterResult>();
var cancellationTokenSource = new CancellationTokenSource();

var filters = new IFilter[] {
        new XmlTypeFilterOneAndQuick(),
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

// Processing time will take between quickest filter and slowest filter.
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
