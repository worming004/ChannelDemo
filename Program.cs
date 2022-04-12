
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

// this is the body content that we wants to discover
var messageContentForQuickStrategy = Encoding.UTF8.GetBytes(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no"" ?>
<person>
    <name>Quick</name>
</person>");
// use this one to trigger success from second strategy
var messageContentForSlowStrategy = Encoding.UTF8.GetBytes(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no"" ?>
<person>
    <name>Slow</name>
</person>");
// Update me depending what scenario you want to test
// var messageToUse = messageContentForQuickStrategy;
var messageToUse = messageContentForQuickStrategy;

// channel is acting like a publish / sub. strategies will send messages of type strategyResult, and main process will read them.
var channel = Channel.CreateUnbounded<StrategyResult>();
var cancellationTokenSource = new CancellationTokenSource();

var strategies = new IStrategy[] {
        new XmlTypeStrategyOneAndQuick(),
        new XmlTypeStrategySecondAndSlow()
    }
    .Select(f => StrategyWrapper.WrapWithTryCatch(f))
    .ToArray();

// start stop watch AFTER Strategies array creation. All calls are independant, meaning that they can be registered as singleton 
var stopWatch = new Stopwatch();

// Run all Strategies in parallel.
stopWatch.Start();

StrategyResult endResult = await AppRun(messageToUse, channel, cancellationTokenSource, strategies);

stopWatch.Stop();

Console.WriteLine(endResult.ToString());
Console.WriteLine($"elapsed time: {stopWatch.ElapsedMilliseconds}");


static async Task<StrategyResult> AppRun(byte[] messageToUse, Channel<StrategyResult> channel, CancellationTokenSource cancellationTokenSource, IStrategy[] strategies)
{
    var allTasksAsSingleOne = Parallel.ForEach(
        strategies,
        async f =>
        {
            await f.IsMatch(new MemoryStream(messageToUse), channel.Writer, cancellationTokenSource.Token);
        }
    );

    StrategyResult endResult = default;

    // Processing time will take between quickest Strategy and slowest Strategy.
    for (var i = 0; i < strategies.Length || allTasksAsSingleOne.IsCompleted; i++)
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

    return endResult;
}