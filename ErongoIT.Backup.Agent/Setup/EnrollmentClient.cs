using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErongoIT.Backup.Agent.Setup;

/// <summary>
/// Talks to the backup server during installation: signs in with an
/// admin account (only for this step), lists customers and plans, and
/// enrolls this PC. Used by "Agent.exe --enroll" and the GUI setup window.
/// </summary>
public sealed class EnrollmentClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public EnrollmentClient(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new ArgumentException("Server address is not a valid URL.", nameof(serverUrl));

        ServerUrl = baseUri.ToString().TrimEnd('/');

        _http = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public string ServerUrl { get; }

    public async Task LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/auth/login",
            new { username, password },
            JsonOptions,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Username or password is incorrect.");

        await EnsureSuccessAsync(response, cancellationToken);

        var login = await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions, cancellationToken)
                    ?? throw new InvalidOperationException("Empty login response.");

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    public async Task<IReadOnlyList<NamedItem>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/customers", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return (await response.Content.ReadFromJsonAsync<List<NamedItem>>(JsonOptions, cancellationToken)
                ?? new List<NamedItem>())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<NamedItem>> GetBackupPlansAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"api/customers/{customerId}/backup-plans", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return (await response.Content.ReadFromJsonAsync<List<NamedItem>>(JsonOptions, cancellationToken)
                ?? new List<NamedItem>())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<EnrollResult> EnrollAsync(
        Guid customerId,
        string deviceName,
        Guid? backupPlanId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/devices/enroll",
            new
            {
                customerId,
                name = deviceName,
                hostname = Environment.MachineName,
                operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                backupPlanId
            },
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<EnrollResult>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Empty enrollment response.");
    }

    public void Dispose() => _http.Dispose();

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new InvalidOperationException(
            $"Server returned HTTP {(int)response.StatusCode}: {body}");
    }

    private sealed record LoginResult(string AccessToken, string TokenType);
}

public sealed record NamedItem(Guid Id, string Name);

public sealed record EnrollResult(
    Guid DeviceId,
    Guid CustomerId,
    string Name,
    Guid? AssignedBackupPlanId,
    string DeviceKey,
    bool IsNewDevice);
