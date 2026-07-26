using PhotoManager.Core.Devices;
using PhotoManager.Core.Import;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Dwa tryby:
//   (bez argumentów)                          -> monitor urządzeń (M1)
//   import <źródło> <cel> [--move|--copy] [--dry-run]  -> import zdjęć (M2)
if (args.Length > 0 && args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
    return await RunImport(args);

if (args.Length > 1 && args[0].Equals("dumpmeta", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(PhotoManager.Core.Metadata.PhotoMetadata.DumpAll(args[1]));
    return 0;
}

if (args.Length > 1 && args[0].Equals("details", StringComparison.OrdinalIgnoreCase))
{
    foreach (var (label, value) in PhotoManager.Core.Metadata.PhotoMetadata.GetDetails(args[1]))
        Console.WriteLine($"{label,-12}: {value}");
    return 0;
}

if (args.Length > 2 && args[0].Equals("movelib", StringComparison.OrdinalIgnoreCase))
{
    var mover = new LibraryMover();
    var rep = await mover.MoveAsync(args[1], args[2],
        new Progress<MoveProgress>(p => Console.WriteLine($"[{p.Current}/{p.Total}] {Path.GetFileName(p.CurrentFile)}")));
    Console.WriteLine($"Skopiowano {rep.Copied}, błędy {rep.Failed}, szybkie={rep.FastMoved}, źródło usunięte={rep.SourceRemoved}");
    foreach (var err in rep.Errors) Console.WriteLine("  ! " + err);
    return 0;
}

return await RunMonitor();

static async Task<int> RunMonitor()
{
    Console.WriteLine("PhotoManager — test wykrywania urządzeń (M1)");
    Console.WriteLine("Podłączaj/odłączaj aparat, telefon lub kartę SD. Ctrl+C kończy.\n");

    await using var monitor = new DeviceMonitor();

    monitor.DeviceConnected += (_, dev) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[+] PODŁĄCZONO  {dev}{(dev.HasDcim ? "  <- karta aparatu (DCIM)" : "")}");
        if (dev.PhotoRoot is not null)
            Console.WriteLine($"    zdjęcia: {dev.PhotoRoot}");
        Console.WriteLine($"    id: {dev.Id}");
        Console.ResetColor();
    };

    monitor.DeviceDisconnected += (_, dev) =>
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[-] ODŁĄCZONO   {dev}");
        Console.ResetColor();
    };

    monitor.Start();

    var done = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
    await done.Task;

    Console.WriteLine("\nZamykanie...");
    return 0;
}

static async Task<int> RunImport(string[] args)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Użycie: import <źródło> <cel> [--move|--copy] [--dry-run]");
        Console.WriteLine("  <źródło>  folder z kartą/aparatem, np. D:\\DCIM");
        Console.WriteLine("  <cel>     folder biblioteki, np. E:\\Zdjecia");
        Console.WriteLine("  --move    przenieś (kasuje źródło po weryfikacji); domyślnie kopiuje");
        Console.WriteLine("  --dry-run pokaż, co by się stało, bez ruszania plików");
        return 2;
    }

    var source = args[1];
    var dest = args[2];
    var mode = args.Any(a => a.Equals("--move", StringComparison.OrdinalIgnoreCase))
        ? ImportMode.Move : ImportMode.Copy;
    var dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));

    if (!Directory.Exists(source))
    {
        Console.WriteLine($"Źródło nie istnieje: {source}");
        return 1;
    }

    var options = new ImportOptions
    {
        DestinationRoot = dest,
        Mode = mode,
        DryRun = dryRun,
    };

    Console.WriteLine($"Import{(dryRun ? " (PRÓBNY)" : "")}: {source}  ->  {dest}");
    Console.WriteLine($"Tryb: {(mode == ImportMode.Move ? "PRZENIEŚ" : "KOPIUJ")}, układ: {options.FolderPattern}\n");

    var importer = new Importer();
    var progress = new Progress<ImportProgress>(p =>
    {
        var tag = p.LastOutcome switch
        {
            ImportOutcome.Imported => "+",
            ImportOutcome.SkippedDuplicate => "=",
            ImportOutcome.Failed => "!",
            _ => " ",
        };
        Console.WriteLine($"[{p.Current}/{p.Total}] {tag} {Path.GetFileName(p.CurrentFile)}");
    });

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    ImportReport report;
    try
    {
        report = await importer.ImportAsync(source, options, progress, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\nPrzerwano.");
        return 1;
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Zaimportowano: {report.Imported}   Duplikaty: {report.Duplicates}   Błędy: {report.Failed}   (razem {report.Total})");
    Console.WriteLine($"Dane: {report.BytesImported / 1_048_576.0:0.0} MB");
    Console.ResetColor();

    foreach (var f in report.Items.Where(i => i.Outcome == ImportOutcome.Failed))
        Console.WriteLine($"  BŁĄD: {Path.GetFileName(f.SourcePath)} — {f.Message}");

    return 0;
}
