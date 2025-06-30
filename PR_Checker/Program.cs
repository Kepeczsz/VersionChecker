
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;


var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var githubToken = config["GitHub:Token"];
var owner = config["GitHub:Owner"];
var repo = config["GitHub:Repo"];

var baseUrl = $"https://api.github.com/repos/{owner}/{repo}";

var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DotNetApp", "1.0"));
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);

var versionRegex = new Regex(@"\d+\.\d+\.\d+\+\w+", RegexOptions.Compiled);

var prToVersion = new Dictionary<string, string>();

Console.WriteLine("Fetching merged PRs...");
var prUrl = $"{baseUrl}/pulls?state=closed&per_page=100";
var prResponse = await client.GetAsync(prUrl);
var prContent = await prResponse.Content.ReadAsStringAsync();
var prs = JsonDocument.Parse(prContent).RootElement.EnumerateArray()
    .Where(pr => pr.GetProperty("merged_at").ValueKind != JsonValueKind.Null)
    .Select(pr => new
    {
        Number = pr.GetProperty("number").GetInt32(),
        Title = pr.GetProperty("title").GetString(),
        MergeCommitSha = pr.GetProperty("merge_commit_sha").GetString()
    })
    .ToList();

foreach (var pr in prs)
{
    Console.WriteLine($"Checking PR #{pr.Number}...");
    var runsUrl = $"{baseUrl}/actions/runs?per_page=100";
    var runsResponse = await client.GetAsync(runsUrl);
    var runsContent = await runsResponse.Content.ReadAsStringAsync();
    var runs = JsonDocument.Parse(runsContent).RootElement.GetProperty("workflow_runs").EnumerateArray();

    var matchingRun = runs.FirstOrDefault(run =>
        run.GetProperty("head_sha").GetString() == pr.MergeCommitSha);

    if (matchingRun.ValueKind == JsonValueKind.Undefined)
        continue;

    var runId = matchingRun.GetProperty("id").GetInt64();
    var logUrl = $"{baseUrl}/actions/runs/{runId}/logs";
    var logResponse = await client.GetAsync(logUrl);

    if (!logResponse.IsSuccessStatusCode)
        continue;

    using var logStream = await logResponse.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(logStream);
    var logText = await reader.ReadToEndAsync();

    var match = versionRegex.Match(logText);
    if (match.Success)
    {
        var version = match.Value;
        prToVersion[version] = $"#{pr.Number} {pr.Title}";
    }
}

Console.WriteLine("\n== PRs by Version ==");
foreach (var group in prToVersion.GroupBy(x => x.Key))
{
    Console.WriteLine($"\nVersion: {group.Key}");
    foreach (var entry in group)
    {
        Console.WriteLine($"  - {entry.Value}");
    }
}
