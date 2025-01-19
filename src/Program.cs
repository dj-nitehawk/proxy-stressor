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

var proxy = new WebProxy(proxyAddress)
{
    Credentials = new NetworkCredential(proxyUsername, proxyPassword)
};

var handler = new HttpClientHandler
{
    Proxy = proxy,
    UseProxy = true
};

using var httpClient = new HttpClient(handler);
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testDuration));
var token = cts.Token;

var successCount = 0;
var failureCount = 0;
var statusCodeCounts = new ConcurrentDictionary<HttpStatusCode, int>();
var totalDurations = new ConcurrentBag<long>();

Console.WriteLine("Starting stress test...");
var stopwatch = Stopwatch.StartNew();
var tasks = new Task[concurrentRequests];

for (var i = 0; i < concurrentRequests; i++)
{
    tasks[i] = Task.Run(
        async () =>
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
                catch
                {
                    requestStopwatch.Stop();
                    Interlocked.Increment(ref failureCount);
                    statusCodeCounts.AddOrUpdate(HttpStatusCode.RequestTimeout, 1, (_, count) => count + 1);
                }
            }
        },
        token);
}

// Progress display loop
var progressTask = Task.Run(
    async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.Clear();
            Console.WriteLine($"Time remaining: {testDuration - stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"Successful requests: {successCount}");
            Console.WriteLine($"Failed requests: {failureCount}");
            Console.WriteLine($"Threads running: {concurrentRequests}");
            await Task.Delay(500); // Update every 500 ms
        }
    });

await Task.WhenAll(tasks);
await progressTask;

stopwatch.Stop();

Console.WriteLine(Environment.NewLine);
Console.WriteLine("Test completed.");
var totalRequests = successCount + failureCount;
var averageRequestDuration = totalDurations.Count > 0 ? totalDurations.Average() : 0;
var requestsPerSecond = totalRequests / stopwatch.Elapsed.TotalSeconds;

Console.WriteLine($"Total requests made: {totalRequests}");
Console.WriteLine($"Successful requests (HTTP 200): {successCount}");
Console.WriteLine($"Failed requests: {failureCount}");
foreach (var kvp in statusCodeCounts)
    Console.WriteLine($"  - {kvp.Key}: {kvp.Value}");
Console.WriteLine($"Average request duration: {averageRequestDuration:F2} ms");
Console.WriteLine($"Requests per second: {requestsPerSecond:F2}");

Console.WriteLine(Environment.NewLine);
Console.WriteLine("Press any key to exit...");
Console.Read();