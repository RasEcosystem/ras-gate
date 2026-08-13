using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace RasGate.Web.Observability;

internal sealed record RacCommandLogContext(
    string Command,
    string? Subcommand,
    string? Target,
    int ArgumentCount)
{
    public static RacCommandLogContext FromArguments(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var command = GetSafeCommand(arguments);

        return new RacCommandLogContext(
            command,
            GetSafeSubcommand(arguments, command),
            GetSafeTarget(arguments, command),
            arguments.Count);
    }

    private static string GetSafeCommand(
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return "<missing>";

        return arguments[0] switch
        {
            "--version" or "-v" => "version",
            "agent" => "agent",
            "cluster" => "cluster",
            "connection" => "connection",
            "counter" => "counter",
            "infobase" => "infobase",
            "lock" => "lock",
            "manager" => "manager",
            "process" => "process",
            "profile" => "profile",
            "rule" => "rule",
            "security-profile" => "security-profile",
            "server" => "server",
            "service" => "service",
            "session" => "session",
            "__test" => "__test",
            _ => "<redacted>"
        };
    }

    private static string? GetSafeSubcommand(
        IReadOnlyList<string> arguments,
        string command)
    {
        if (arguments.Count < 2 ||
            command is "<missing>" or "<redacted>" or "version")
            return null;

        return arguments[1] switch
        {
            "activate" => "activate",
            "apply" => "apply",
            "clear" => "clear",
            "copy" => "copy",
            "create" => "create",
            "deactivate" => "deactivate",
            "disconnect" => "disconnect",
            "drop" => "drop",
            "get" => "get",
            "info" => "info",
            "insert" => "insert",
            "kill" => "kill",
            "list" => "list",
            "move" => "move",
            "pause" => "pause",
            "remove" => "remove",
            "resume" => "resume",
            "set" => "set",
            "start" => "start",
            "stop" => "stop",
            "terminate" => "terminate",
            "update" => "update",
            "delay" when command == "__test" => "delay",
            "exit" when command == "__test" => "exit",
            "large-output" when command == "__test" => "large-output",
            "pid-delay" when command == "__test" => "pid-delay",
            "spawn-pipe-holder" when command == "__test" =>
                "spawn-pipe-holder",
            "stderr" when command == "__test" => "stderr",
            "stdout" when command == "__test" => "stdout",
            _ => "<redacted>"
        };
    }

    private static string? GetSafeTarget(
        IReadOnlyList<string> arguments,
        string command)
    {
        if (command is "<missing>" or "<redacted>" or "version")
            return null;

        return TryParseEndpoint(
            arguments[^1],
            out var endpoint)
            ? endpoint
            : null;
    }

    private static bool TryParseEndpoint(
        string value,
        out string endpoint)
    {
        endpoint = "";

        if (string.IsNullOrWhiteSpace(value) || value.Length > 263)
            return false;

        string host;
        string portText;

        if (value[0] == '[')
        {
            var closingBracket = value.IndexOf(
                ']',
                StringComparison.Ordinal);

            if (closingBracket <= 1 ||
                closingBracket + 2 >= value.Length ||
                value[closingBracket + 1] != ':')
                return false;

            host = value[1..closingBracket];
            portText = value[(closingBracket + 2)..];

            if (!IPAddress.TryParse(host, out var ipAddress))
                return false;

            endpoint = ipAddress.AddressFamily ==
                       AddressFamily.InterNetworkV6
                ? "ipv6"
                : "ipv4";
        }
        else
        {
            var separator = value.LastIndexOf(':');

            if (separator <= 0 ||
                separator == value.Length - 1 ||
                value.AsSpan(0, separator).Contains(':'))
                return false;

            host = value[..separator];
            portText = value[(separator + 1)..];

            var hostNameType = Uri.CheckHostName(host);

            if (hostNameType == UriHostNameType.Unknown)
                return false;

            endpoint = hostNameType switch
            {
                UriHostNameType.IPv4 => "ipv4",
                UriHostNameType.IPv6 => "ipv6",
                _ => "dns"
            };
        }

        if (!int.TryParse(
                portText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var port) ||
            port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            return false;

        return true;
    }
}