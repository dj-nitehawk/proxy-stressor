using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

Console.WriteLine("Enter proxy address (including port):");
var proxyAddress = Console.ReadLine();

Console.WriteLine("Enter proxy username:");
var proxyUsername = Console.ReadLine();

Console.WriteLine("Enter proxy password:");
var proxyPassword = Console.ReadLine();

Console.WriteLine("Enter number of simultaneous requests:");

if (!int.TryParse(Console.ReadLine(), out var concurrentRequests) || concurrentRequests <= 0)
{
    Console.WriteLine("Invalid number of simultaneous requests.");

    return;
}

Console.WriteLine("Enter test duration in seconds:");

if (!int.TryParse(Console.ReadLine(), out var testDuration) || testDuration <= 0)
{
    Console.WriteLine("Invalid test duration.");

    return;
}

Console.WriteLine("Enter website URL to make requests to:");
var websiteUrl = Console.ReadLine();

if (string.IsNullOrWhiteSpace(websiteUrl))
{
    Console.WriteLine("Invalid website URL.");

    return;
}

var handler = new SocketsHttpHandler
{
    Proxy = new WebProxy(proxyAddress, true, null, new NetworkCredential(proxyUsername, proxyPassword)),
    UseProxy = true,
    ConnectTimeout = TimeSpan.FromSeconds(5)
};

using var httpClient = new HttpClient(handler);
httpClient.Timeout = TimeSpan.FromSeconds(10);
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testDuration));
var token = cts.Token;

var successCount = 0;
var failureCount = 0;
var statusCodeCounts = new ConcurrentDictionary<HttpStatusCode, int>();
var totalDurations = new ConcurrentBag<long>();

Console.WriteLine("Starting stress test...");
var stopwatch = Stopwatch.StartNew();

async ValueTask MakeRequests(int _, CancellationToken __)
{
    while (!token.IsCancellationRequested)
    {
        var requestStopwatch = Stopwatch.StartNew();

        try
        {
            var response = await httpClient.GetAsync(websiteUrl, token);
            requestStopwatch.Stop();
            totalDurations.Add(requestStopwatch.ElapsedMilliseconds);

            if (response.IsSuccessStatusCode)
                Interlocked.Increment(ref successCount);
            else
            {
                Interlocked.Increment(ref failureCount);
                statusCodeCounts.AddOrUpdate(response.StatusCode, 1, (_, count) => count + 1);
            }
        }
        catch (Exception ex)
        {
            if (ex is not TaskCanceledException)
            {
                requestStopwatch.Stop();
                Interlocked.Increment(ref failureCount);
                statusCodeCounts.AddOrUpdate(HttpStatusCode.RequestTimeout, 1, (_, count) => count + 1);
            }
        }
    }
}

var requestsTask = Parallel.ForAsync(0, concurrentRequests, MakeRequests);

_ = Task.Run(
    () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.Clear();
            Console.WriteLine($"Threads running: {concurrentRequests}");
            Console.WriteLine($"Successful requests: {successCount}");
            Console.WriteLine($"Failed requests: {failureCount}");
            Console.WriteLine($"Time remaining (sec): {testDuration - stopwatch.Elapsed.TotalSeconds:N0}");
            Thread.Sleep(500);
        }
    });

await requestsTask;
stopwatch.Stop();

Console.WriteLine(Environment.NewLine);
Console.WriteLine("Test completed!");
Console.WriteLine(Environment.NewLine);

var totalRequests = successCount + failureCount;
var averageRequestDuration = TimeSpan.FromMilliseconds(totalDurations.DefaultIfEmpty(0).Average()).TotalSeconds;
var requestsPerSecond = totalRequests / stopwatch.Elapsed.TotalSeconds;

Console.WriteLine($"Total requests made: {totalRequests}");
Console.WriteLine($"Successful requests (HTTP 200): {successCount}");
Console.WriteLine($"Failed requests: {failureCount}");
foreach (var kvp in statusCodeCounts)
    Console.WriteLine($"  - {kvp.Key}: {kvp.Value}");
Console.WriteLine($"Average request duration (sec): {averageRequestDuration:N0}");
Console.WriteLine($"Requests per second: {requestsPerSecond:N0}");

Console.WriteLine(Environment.NewLine);
Console.WriteLine("Press any key to exit...");
Console.Read();