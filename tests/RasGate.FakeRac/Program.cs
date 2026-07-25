using System.Globalization;

return await FakeRac.RunAsync(args);

internal static class FakeRac
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            await Console.Error.WriteLineAsync(
                "FakeRac: command is not specified.");

            return 1;
        }

        return args[0] switch
        {
            "--version" or "-v" => WriteVersion(),
            "__test" => await ExecuteTestCommandAsync(args),
            _ => WriteUnknownCommand(args)
        };
    }

    private static int WriteVersion()
    {
        Console.WriteLine(
            "FakeRac: Remote Administrative Client 1.0.0");

        return 0;
    }

    private static async Task<int> ExecuteTestCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            await Console.Error.WriteLineAsync(
                "FakeRac: test mode is not specified.");

            return 1;
        }

        switch (args[1])
        {
            case "stdout":
                Console.WriteLine(
                    string.Join(' ', args.Skip(2)));

                return 0;

            case "stderr":
                await Console.Error.WriteLineAsync(
                    string.Join(' ', args.Skip(2)));

                return 1;

            case "exit":
                return ParseExitCode(args);

            case "delay":
                return await DelayAsync(args);

            case "large-output":
                return WriteLargeOutput(args);

            default:
                await Console.Error.WriteLineAsync(
                    $"FakeRac: unknown test mode '{args[1]}'.");

                return 1;
        }
    }

    private static int ParseExitCode(string[] args)
    {
        if (args.Length >= 3 &&
            int.TryParse(
                args[2],
                CultureInfo.InvariantCulture,
                out var exitCode))
            return exitCode;

        return 1;
    }

    private static async Task<int> DelayAsync(string[] args)
    {
        var milliseconds =
            args.Length >= 3 &&
            int.TryParse(args[2], out var value)
                ? value
                : 1000;

        await Task.Delay(milliseconds);

        Console.WriteLine($"Completed after {milliseconds} ms.");

        return 0;
    }

    private static int WriteLargeOutput(string[] args)
    {
        var bytes =
            args.Length >= 3 &&
            int.TryParse(args[2], out var value)
                ? value
                : 1024;

        Console.Write(new string('X', bytes));

        return 0;
    }

    private static int WriteUnknownCommand(string[] args)
    {
        Console.Error.WriteLine(
            $"FakeRac: unknown command '{string.Join(' ', args)}'.");

        return 1;
    }
}