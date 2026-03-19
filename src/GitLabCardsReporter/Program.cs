using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

var configuration = BuildConfiguration();
var settings = LoadSettings(configuration, args);

if (string.IsNullOrWhiteSpace(settings.PrivateToken) || string.IsNullOrWhiteSpace(settings.Project))
{
    PrintUsage();
    return 1;
}

using var httpClient = CreateHttpClient(settings);
var gitLabClient = new GitLabClient(httpClient, settings.BaseUrl);

try
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine($"Consultando projeto: {settings.Project}");

    var issues = await gitLabClient.GetIssuesAsync(settings.Project, settings.IncludeClosed, settings.PerPage);

    if (issues.Count == 0)
    {
        Console.WriteLine("Nenhum card/issue encontrado para os filtros informados.");
        return 0;
    }

    var ordered = issues
        .OrderBy(i => i.Iid)
        .Select(i => new CardReportRow(
            Number: i.Iid,
            Title: i.Title,
            Labels: i.Labels?.Count > 0 ? string.Join(", ", i.Labels) : "(sem labels)",
            TimeSpentSeconds: i.time_stats?.total_time_spent ?? 0,
            HumanTimeSpent: string.IsNullOrWhiteSpace(i.time_stats?.human_total_time_spent) ? "0m" : i.time_stats!.human_total_time_spent!,
            State: i.State ?? string.Empty,
            WebUrl: i.web_url ?? string.Empty,
            updated_at: i.updated_at.Value.ToString("dd/MM/yyyy") ?? string.Empty,
            Responsaveis: i.Assignees?.Count > 0 ? string.Join(", ",i.Assignees.Select(c => c.Name).ToArray()) :"Sem Responsável"))
        .ToList();

    PrintTable(ordered);

    if (!string.IsNullOrWhiteSpace(settings.OutputCsvPath))
    {
        CsvExporter.Write(DateTime.Now.ToString("ddMMyyyy_hhmmss_") + settings.Project.Replace("/","_") + "_" + settings.OutputCsvPath, ordered);
        Console.WriteLine();
        Console.WriteLine($"CSV gerado em: {Path.GetFullPath(settings.OutputCsvPath)}");
    }

    return 0;
}
catch (GitLabApiException ex)
{
    Console.Error.WriteLine($"Erro na API do GitLab: {(int)ex.StatusCode} - {ex.Message}");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Erro inesperado: {ex.Message}");
    return 3;
}

static IConfigurationRoot BuildConfiguration()
{
    return new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        //.AddEnvironmentVariables(prefix: "GITLAB_REPORTER_")
        .Build();
}

static ReporterSettings LoadSettings(IConfiguration configuration, string[] args)
{
    var settings = configuration.GetSection("GitLab").Get<ReporterSettings>() ?? new ReporterSettings();

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        if (arg.Equals("--base-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            settings.BaseUrl = args[++i];
        else if (arg.Equals("--token", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            settings.PrivateToken = args[++i];
        else if (arg.Equals("--project", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            settings.Project = args[++i];
        else if (arg.Equals("--include-closed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && bool.TryParse(args[++i], out var includeClosed))
            settings.IncludeClosed = includeClosed;
        else if (arg.Equals("--per-page", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out var perPage))
            settings.PerPage = Math.Clamp(perPage, 1, 100);
        else if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            settings.OutputCsvPath = args[++i];
    }

    settings.BaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://gitlab.com" : settings.BaseUrl.TrimEnd('/');
    settings.PerPage = settings.PerPage is > 0 and <= 100 ? settings.PerPage : 100;
    return settings;
}

static HttpClient CreateHttpClient(ReporterSettings settings)
{
    var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    httpClient.DefaultRequestHeaders.Add("PRIVATE-TOKEN", settings.PrivateToken);
    httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitLabCardsReporter", "1.0.0"));

    return httpClient;
}

static void PrintTable(IReadOnlyCollection<CardReportRow> rows)
{
    const int numberWidth = 8;
    const int stateWidth = 10;
    const int timeWidth = 14;
    const int labelsWidth = 35;
    const int titleWidth = 55;

    Console.WriteLine();
    Console.WriteLine(
        Pad("Issue", numberWidth) + " | " +
        Pad("Status", stateWidth) + " | " +
        Pad("Tempo", timeWidth) + " | " +
        Pad("Labels", labelsWidth) + " | " +
        Pad("Título", titleWidth));

    Console.WriteLine(new string('-', numberWidth + stateWidth + timeWidth + labelsWidth + titleWidth + 12));

    foreach (var row in rows)
    {
        Console.WriteLine(
            Pad("#" + row.Number, numberWidth) + " | " +
            Pad(row.State, stateWidth) + " | " +
            Pad(row.HumanTimeSpent, timeWidth) + " | " +
            Pad(row.Labels, labelsWidth) + " | " +
            Pad(row.Title, titleWidth));
    }

    Console.WriteLine();
    Console.WriteLine($"Total de cards encontrados: {rows.Count}");
    Console.WriteLine($"Tempo total: {FormatDuration(rows.Sum(x => x.TimeSpentSeconds))}");
}

static string Pad(string? value, int width)
{
    var safe = value ?? string.Empty;
    if (safe.Length <= width)
        return safe.PadRight(width);

    return safe[..Math.Max(0, width - 3)] + "...";
}

static string FormatDuration(int totalSeconds)
{
    if (totalSeconds <= 0)
        return "0m";

    var ts = TimeSpan.FromSeconds(totalSeconds);
    var parts = new List<string>();

    if (ts.Days > 0) parts.Add($"{ts.Days}d");
    if (ts.Hours > 0) parts.Add($"{ts.Hours}h");
    if (ts.Minutes > 0) parts.Add($"{ts.Minutes}m");

    if (parts.Count == 0)
        parts.Add($"{Math.Max(1, ts.Seconds)}s");

    return string.Join(' ', parts);
}

static void PrintUsage()
{
    Console.WriteLine("Uso:");
    Console.WriteLine("  dotnet run --project src/GitLabCardsReporter/GitLabCardsReporter.csproj -- \\");
    Console.WriteLine("    --token <SEU_TOKEN> --project <id-ou-caminho> [--base-url https://gitlab.com] [--include-closed true] [--per-page 100] [--output cards-report.csv]");
    Console.WriteLine();
    Console.WriteLine("Exemplos:");
    Console.WriteLine("  dotnet run --project src/GitLabCardsReporter/GitLabCardsReporter.csproj -- --token glpat-xxx --project meu-grupo/meu-projeto");
    Console.WriteLine("  dotnet run --project src/GitLabCardsReporter/GitLabCardsReporter.csproj -- --token glpat-xxx --project 12345 --output resultado.csv");
    Console.WriteLine();
    Console.WriteLine("Variáveis de ambiente suportadas:");
    Console.WriteLine("  GITLAB_REPORTER_GitLab__BaseUrl");
    Console.WriteLine("  GITLAB_REPORTER_GitLab__PrivateToken");
    Console.WriteLine("  GITLAB_REPORTER_GitLab__Project");
    Console.WriteLine("  GITLAB_REPORTER_GitLab__IncludeClosed");
    Console.WriteLine("  GITLAB_REPORTER_GitLab__PerPage");
    Console.WriteLine("  GITLAB_REPORTER_GitLab__OutputCsvPath");
}

internal sealed class GitLabClient(HttpClient httpClient, string baseUrl)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<GitLabIssue>> GetIssuesAsync(string project, bool includeClosed, int perPage)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var state = includeClosed ? "all" : "opened";
        var page = 1;
        var issues = new List<GitLabIssue>();

        while (true)
        {
            var url = $"{baseUrl}/api/v4/projects/{encodedProject}/issues?scope=all&state={state}&per_page={perPage}&page={page}";
            using var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new GitLabApiException(response.StatusCode, errorBody);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var pageItems = await JsonSerializer.DeserializeAsync<List<GitLabIssue>>(stream, JsonOptions) ?? new List<GitLabIssue>();
            issues.AddRange(pageItems);

            var nextPage = response.Headers.TryGetValues("X-Next-Page", out var values)
                ? values.FirstOrDefault()
                : null;

            if (string.IsNullOrWhiteSpace(nextPage))
                break;

            page = int.Parse(nextPage, CultureInfo.InvariantCulture);
        }

        return issues;
    }
}

internal static class CsvExporter
{
    public static void Write(string path, IReadOnlyCollection<CardReportRow> rows)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("NumeroAtividade;Titulo;Labels;TempoTotalGastoEmMinutos;TempoTotalGasto;Status;DataUltimaAtualização;Responsaveis;WebUrl");

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(';',
                row.Number,
                EscapeCsv(row.Title),
                EscapeCsv(row.Labels),                
                row.TimeSpentSeconds,                
                EscapeCsv(row.HumanTimeSpent),
                EscapeCsv(row.State),
                EscapeCsv(row.updated_at),
                row.Responsaveis,
                EscapeCsv(row.WebUrl)));
        }
    }

    private static string EscapeCsv(string? value)
    {
        var safe = value ?? string.Empty;
        if (!safe.Contains(';') && !safe.Contains('"') && !safe.Contains('\n') && !safe.Contains('\r'))
            return safe;

        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}

internal sealed class GitLabApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

internal sealed class ReporterSettings
{
    public string BaseUrl { get; set; } = "https://gitlab.com";
    public string PrivateToken { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public bool IncludeClosed { get; set; } = true;
    public int PerPage { get; set; } = 100;
    public string OutputCsvPath { get; set; } = "cards-report.csv";
}

internal sealed record CardReportRow(
    int Number,
    string Title,
    string Labels,
    int TimeSpentSeconds,
    string HumanTimeSpent,
    string State,
    string WebUrl, 
    string updated_at,
    string Responsaveis);

internal sealed class GitLabIssue
{
    public int Iid { get; set; }
    public string? Title { get; set; }
    public string? State { get; set; }
    public string? web_url { get; set; }
    public List<string>? Labels { get; set; }
    public List<GitLabUser> Assignees { get; set; } = new();
    public GitLabTimeStats? time_stats { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class GitLabUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class GitLabTimeStats
{
    public int total_time_spent { get; set; }
    public string? human_total_time_spent { get; set; }
}
