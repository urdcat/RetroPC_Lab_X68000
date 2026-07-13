using M68kAsm;

return await RunAsync(args);

static Task<int> RunAsync(string[] args)
{
    if (args.Length == 1 && args[0] == "selftest")
    {
        return Task.FromResult(RunSelfTest());
    }

    if (args.Length == 2 && args[0] == "check")
    {
        return Task.FromResult(CheckFile(args[1]));
    }

    Console.Error.WriteLine("使い方:");
    Console.Error.WriteLine("  m68kasm check <source.m68>");
    Console.Error.WriteLine("  m68kasm selftest");
    return Task.FromResult(2);
}

static int CheckFile(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"入力ファイルがありません: {path}");
        return 2;
    }

    var result = new LanguageParser().Parse(File.ReadAllText(path));
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine($"{path}:{diagnostic}");
    }

    if (!result.Success)
    {
        return 1;
    }

    foreach (var structure in result.Module.Structures)
    {
        Console.WriteLine($"struct {structure.Name}: {structure.Fields.Count} field(s)");
    }

    foreach (var procedure in result.Module.Procedures)
    {
        var ending = procedure.EmitsRts ? "RTS" : "no RTS";
        Console.WriteLine($"procedure {procedure.Name}: {procedure.Locals.Count} local(s), {procedure.Statements.Count} statement(s), {ending}");
    }

    return 0;
}

static int RunSelfTest()
{
    const string source = """
        struct Actor {
          x: word
          y: word
          flags: byte
        }

        [update_actor
          local dx: word, dy: word
          d0.w = d1.w + #1 ; dx = d0.w // 1行に複数命令
        ]/

        [finish
          d0.l += #1
        ]
        """;

    var result = new LanguageParser().Parse(source);
    if (!result.Success || result.Module.Structures.Count != 1 || result.Module.Procedures.Count != 2)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine("selftest failed: パース結果が期待と異なります");
        return 1;
    }

    var update = result.Module.Procedures[0];
    var finish = result.Module.Procedures[1];
    if (update.EmitsRts || update.Locals.Count != 2 || update.Statements.Count != 2 || !finish.EmitsRts)
    {
        Console.Error.WriteLine("selftest failed: 手続き終端または複数命令の扱いが不正です");
        return 1;
    }

    var invalid = new LanguageParser().Parse("""
        [invalid
          d0 = d1 + #1
        ]
        """);
    if (invalid.Success)
    {
        Console.Error.WriteLine("selftest failed: 幅指定のないデータレジスタを受理しました");
        return 1;
    }

    Console.WriteLine("selftest passed: procedure, local, structure, algebra and multi-statement syntax");
    return 0;
}
