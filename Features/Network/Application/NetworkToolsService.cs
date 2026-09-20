using System.Net;
using System.Net.NetworkInformation;
using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Nexora.Features.Network.Domain;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Network.Application;

public sealed class NetworkToolsService : INetworkToolsService
{
    private readonly IProcessRunner _runner;
    private readonly GameLoopOptions _gameLoop;

    public NetworkToolsService(IProcessRunner runner, GameLoopOptions? gameLoop = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    public OperationResult ChangeDns(string primary, string secondary)
    {
        if (!IsValidIpAddress(primary) || !IsValidIpAddress(secondary))
        {
            return OperationResult.Fail("Invalid DNS server address. Both addresses must be valid IP addresses.");
        }

        var script = $"$servers = [string[]]@({ProcessText.Quote(primary)}, {ProcessText.Quote(secondary)}); " +
                     "$adapters = @(Get-NetAdapter -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' -and " +
                     "@(Get-NetIPAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | " +
                     "Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' }).Count -gt 0 }); " +
                     "if ($adapters.Count -eq 0) { Write-Error 'No active network adapters were found.'; exit 2 }; " +
                     "$failed = New-Object 'System.Collections.Generic.List[string]'; " +
                     "$applied = 0; " +
                     "foreach ($adapter in $adapters) { try { Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $servers -ErrorAction Stop; $applied++ } " +
                     "catch { [void]$failed.Add(([string]$adapter.Name + ': ' + $_.Exception.Message)) } }; " +
                     "ipconfig /flushdns | Out-Null; " +
                     "$notApplied = New-Object 'System.Collections.Generic.List[string]'; " +
                     "foreach ($adapter in $adapters) { $actual = @(Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | " +
                     "Select-Object -ExpandProperty ServerAddresses); " +
                     "if ($actual.Count -ne $servers.Count -or (($actual -join '|') -ne ($servers -join '|'))) { [void]$notApplied.Add([string]$adapter.Name) } }; " +
                     "if ($applied -eq 0 -or $failed.Count -gt 0 -or $notApplied.Count -gt 0) { " +
                     "Write-Error ('Could not verify DNS on: ' + (($failed + $notApplied) -join ', ')); exit 2 }; " +
                     "Write-Output ('NEXORA_DNS_OK|' + $applied)";
        var result = _runner.RunPowerShell(script, _gameLoop.Timeouts.DnsChangeTimeout);
        if (!result.Succeeded || !result.StandardOutput.Contains("NEXORA_DNS_OK|", StringComparison.Ordinal))
        {
            return OperationResult.Fail($"Could not change DNS settings. {ProcessText.GetError(result)}");
        }

        var marker = result.StandardOutput.Trim().Split('|').LastOrDefault();
        return OperationResult.Ok($"DNS changed to {primary} / {secondary} on {marker ?? "active adapters"}.");
    }

    public async Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default)
    {
        try
        {
            using var ping = new Ping();
            long? lowest = null;
            var timeout = TimeSpan.FromMilliseconds(_gameLoop.Timeouts.DnsPingTimeoutMilliseconds);
            for (var attempt = 0; attempt < _gameLoop.Timeouts.DnsPingAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reply = await ping.SendPingAsync(host, timeout, cancellationToken: cancellationToken);
                if (reply.Status == IPStatus.Success && (lowest is null || reply.RoundtripTime < lowest))
                {
                    lowest = reply.RoundtripTime;
                }
            }

            return lowest is null ? null : (int)lowest.Value;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidIpAddress(string? value) => IPAddress.TryParse(value, out _);
}
