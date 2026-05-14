using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal sealed class Options
{
    private Options(
        IReadOnlyList<string> processNames,
        IReadOnlySet<int> processIds,
        float step,
        float minimum,
        float maximum,
        bool showHelp,
        bool listSessions,
        bool listDevices)
    {
        ProcessNames = processNames;
        ProcessIds = processIds;
        Step = step;
        Minimum = minimum;
        Maximum = maximum;
        ShowHelp = showHelp;
        ListSessions = listSessions;
        ListDevices = listDevices;
    }

    public IReadOnlyList<string> ProcessNames { get; }

    public IReadOnlySet<int> ProcessIds { get; }

    public float Step { get; }

    public float Minimum { get; }

    public float Maximum { get; }

    public bool ShowHelp { get; }

    public bool ListSessions { get; }

    public bool ListDevices { get; }

    public string TargetDescription
    {
        get
        {
            var parts = new List<string>();
            if (ProcessNames.Count > 0)
            {
                parts.Add(string.Join(", ", ProcessNames));
            }

            if (ProcessIds.Count > 0)
            {
                parts.Add("PID " + string.Join(", ", ProcessIds));
            }

            return string.Join(" / ", parts);
        }
    }

    public static Options Parse(string[] args)
    {
        var processNames = new List<string>();
        var processIds = new HashSet<int>();
        var step = 0.05f;
        var minimum = 0.0f;
        var maximum = 1.0f;
        var showHelp = false;
        var listSessions = false;
        var listDevices = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-h":
                case "--help":
                case "/?":
                    showHelp = true;
                    break;

                case "--list":
                case "--sessions":
                    listSessions = true;
                    break;

                case "--devices":
                    listDevices = true;
                    break;

                case "-p":
                case "--process":
                    foreach (var name in SplitRequiredValue(args, ref index, arg))
                    {
                        processNames.Add(NormalizeProcessName(name));
                    }

                    break;

                case "--pid":
                    foreach (var pidText in SplitRequiredValue(args, ref index, arg))
                    {
                        if (!int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
                        {
                            throw new ArgumentException($"PID invalido: {pidText}");
                        }

                        processIds.Add(pid);
                    }

                    break;

                case "-s":
                case "--step":
                    step = ParseVolumeValue(RequiredValue(args, ref index, arg), arg);
                    if (step <= 0)
                    {
                        throw new ArgumentException("O passo precisa ser maior que zero.");
                    }

                    break;

                case "--min":
                    minimum = ParseVolumeValue(RequiredValue(args, ref index, arg), arg);
                    break;

                case "--max":
                    maximum = ParseVolumeValue(RequiredValue(args, ref index, arg), arg);
                    break;

                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Opcao desconhecida: {arg}");
                    }

                    processNames.Add(NormalizeProcessName(arg));
                    break;
            }
        }

        if (!showHelp && !listSessions && !listDevices && processNames.Count == 0 && processIds.Count == 0)
        {
            processNames.Add("Spotify");
        }

        if (minimum < 0 || maximum > 1 || minimum > maximum)
        {
            throw new ArgumentException("Use limites entre 0 e 100%, com --min menor ou igual a --max.");
        }

        return new Options(
            processNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            processIds,
            step,
            minimum,
            maximum,
            showHelp,
            listSessions,
            listDevices);
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            Uso:
              volume-key-router.exe
              volume-key-router.exe --cli [processo] [opcoes]

            Sem argumentos, abre a interface grafica.

            Exemplos CLI:
              volume-key-router.exe --cli Spotify
              volume-key-router.exe --cli --process Spotify --step 3
              volume-key-router.exe --cli --list
              volume-key-router.exe --cli --devices

            Opcoes CLI:
              -p, --process NOME   Nome do processo alvo, sem depender da API do app.
              --pid PID            PID alvo. Pode combinar com --process.
              -s, --step VALOR     Passo por tecla. Aceita 5, 5% ou 0.05. Padrao: 5%.
              --min VALOR          Volume minimo permitido. Padrao: 0%.
              --max VALOR          Volume maximo permitido. Padrao: 100%.
              --list               Lista as sessoes de audio por dispositivo.
              --devices            Lista os dispositivos de saida.
              -h, --help           Mostra esta ajuda.
            """);
    }

    private static IEnumerable<string> SplitRequiredValue(string[] args, ref int index, string option)
    {
        var value = RequiredValue(args, ref index, option);
        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string RequiredValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Faltou valor para {option}.");
        }

        return args[++index];
    }

    private static float ParseVolumeValue(string value, string option)
    {
        var trimmed = value.Trim();
        var isPercent = trimmed.EndsWith('%');
        if (isPercent)
        {
            trimmed = trimmed[..^1];
        }

        if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Valor invalido para {option}: {value}");
        }

        if (isPercent || parsed >= 1)
        {
            parsed /= 100.0f;
        }

        return Math.Clamp(parsed, 0.0f, 1.0f);
    }

    private static string NormalizeProcessName(string value)
    {
        var fileName = Path.GetFileNameWithoutExtension(value.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Nome de processo vazio.");
        }

        return fileName;
    }
}
