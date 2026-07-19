using System.Reflection;
using M68kAsm;

return await RunAsync(args);

static Task<int> RunAsync(string[] args)
{
    if (args.Length == 1 && args[0] is "--version" or "-V")
    {
        var version = typeof(LanguageParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
        Console.WriteLine($"m68kasm {version}");
        return Task.FromResult(0);
    }

    if (args.Length == 1 && args[0] == "selftest")
    {
        return Task.FromResult(RunSelfTest());
    }

    if (args.Length >= 2 && args[0] == "check")
    {
        return Task.FromResult(CheckCommand(args));
    }

    if (args.Length >= 2 && args[0] == "assemble")
    {
        return Task.FromResult(AssembleCommand(args));
    }

    Console.Error.WriteLine("使い方:");
    Console.Error.WriteLine("  m68kasm --version");
    Console.Error.WriteLine("  m68kasm check <source.m68> [--base-address <address>] [--cpu 68000|68010|68020|68030]");
    Console.Error.WriteLine("  m68kasm assemble <source.m68> -o <output.bin> [--base-address <address>] [--cpu 68000|68010|68020|68030]");
    Console.Error.WriteLine("  m68kasm selftest");
    return Task.FromResult(2);
}

static int CheckCommand(string[] args)
{
    uint baseAddress = 0;
    var cpu = CpuModel.Mc68000;
    for (var index = 2; index < args.Length; index++)
    {
        if (args[index] == "--base-address" && index + 1 < args.Length)
        {
            if (!TryParseBaseAddress(args[++index], out baseAddress))
            {
                Console.Error.WriteLine($"ベースアドレスを解釈できません: {args[index]}");
                return 2;
            }

            continue;
        }

        if (args[index] == "--cpu" && index + 1 < args.Length)
        {
            if (!CpuModelParser.TryParse(args[++index], out cpu))
            {
                Console.Error.WriteLine($"CPUを解釈できません: {args[index]}（68000、68010、68020、68030から選択）");
                return 2;
            }

            continue;
        }

        Console.Error.WriteLine($"不明または値が不足したオプションです: {args[index]}");
        return 2;
    }

    return CheckFile(args[1], baseAddress, cpu);
}

static int AssembleCommand(string[] args)
{
    string? outputPath = null;
    uint baseAddress = 0;
    var cpu = CpuModel.Mc68000;
    for (var index = 2; index < args.Length; index++)
    {
        if (args[index] == "-o" && index + 1 < args.Length)
        {
            outputPath = args[++index];
            continue;
        }

        if (args[index] == "--base-address" && index + 1 < args.Length)
        {
            if (!TryParseBaseAddress(args[++index], out baseAddress))
            {
                Console.Error.WriteLine($"ベースアドレスを解釈できません: {args[index]}");
                return 2;
            }

            continue;
        }

        if (args[index] == "--cpu" && index + 1 < args.Length)
        {
            if (!CpuModelParser.TryParse(args[++index], out cpu))
            {
                Console.Error.WriteLine($"CPUを解釈できません: {args[index]}（68000、68010、68020、68030から選択）");
                return 2;
            }

            continue;
        }

        Console.Error.WriteLine($"不明または値が不足したオプションです: {args[index]}");
        return 2;
    }

    if (outputPath is null)
    {
        Console.Error.WriteLine("出力先を-oで指定してください");
        return 2;
    }

    return AssembleFile(args[1], outputPath, baseAddress, cpu);
}

static bool TryParseBaseAddress(string text, out uint value)
{
    value = 0;
    var token = text.Trim();
    try
    {
        if (token.StartsWith('$'))
        {
            value = Convert.ToUInt32(token[1..], 16);
        }
        else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = Convert.ToUInt32(token[2..], 16);
        }
        else
        {
            value = Convert.ToUInt32(token, 10);
        }

        return true;
    }
    catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
    {
        return false;
    }
}

static int AssembleFile(string sourcePath, string outputPath, uint baseAddress, CpuModel cpu)
{
    if (!File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"入力ファイルがありません: {sourcePath}");
        return 2;
    }

    var parseResult = new LanguageParser().Parse(File.ReadAllText(sourcePath), sourcePath);
    foreach (var diagnostic in parseResult.Diagnostics)
    {
        Console.Error.WriteLine($"{sourcePath}:{diagnostic}");
    }

    if (!parseResult.Success)
    {
        return 1;
    }

    var assemblyResult = new BinaryAssembler().Assemble(parseResult.Module, baseAddress, cpu);
    foreach (var diagnostic in assemblyResult.Diagnostics)
    {
        Console.Error.WriteLine($"{sourcePath}:{diagnostic}");
    }

    if (!assemblyResult.Success)
    {
        return 1;
    }

    try
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullOutputPath, assemblyResult.Bytes);
        Console.WriteLine($"wrote {assemblyResult.Bytes.Length} byte(s): {fullOutputPath}");
        foreach (var constant in parseResult.Module.Constants.OrderBy(item => item.Key))
        {
            Console.WriteLine($"  {constant.Key} = {constant.Value} (constant)");
        }

        foreach (var symbol in assemblyResult.Symbols.OrderBy(item => item.Value))
        {
            Console.WriteLine($"  {symbol.Key} = ${symbol.Value:x8}");
        }
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"出力ファイルを書き込めません: {outputPath}: {exception.Message}");
        return 2;
    }

    return 0;
}

static int CheckFile(string path, uint baseAddress, CpuModel cpu)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"入力ファイルがありません: {path}");
        return 2;
    }

    var parseResult = new LanguageParser().Parse(File.ReadAllText(path), path);
    foreach (var diagnostic in parseResult.Diagnostics)
    {
        Console.Error.WriteLine($"{path}:{diagnostic}");
    }

    if (!parseResult.Success)
    {
        return 1;
    }

    var assemblyResult = new BinaryAssembler().Assemble(parseResult.Module, baseAddress, cpu);
    foreach (var diagnostic in assemblyResult.Diagnostics)
    {
        Console.Error.WriteLine($"{path}:{diagnostic}");
    }

    if (!assemblyResult.Success)
    {
        return 1;
    }

    foreach (var constant in parseResult.Module.Constants.OrderBy(item => item.Key))
    {
        Console.WriteLine($"constant {constant.Key} = {constant.Value}");
    }

    foreach (var structure in parseResult.Module.Structures)
    {
        Console.WriteLine($"struct {structure.Name}: {structure.Fields.Count} field(s)");
    }

    foreach (var procedure in parseResult.Module.Procedures)
    {
        var ending = procedure.EmitsRts ? "RTS" : "no RTS";
        Console.WriteLine($"procedure {procedure.Name}: {procedure.Locals.Count} local(s), {procedure.Statements.Count} statement(s), {ending}");
    }

    Console.WriteLine($"check passed: {assemblyResult.Bytes.Length} byte(s), MC{cpu.ToCommandLineName()}");
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

    var assembly = new BinaryAssembler().Assemble(result.Module);
    byte[] expected =
    [
        0x4e, 0x56, 0xff, 0xfc, // LINK A6,#-4
        0x30, 0x01,             // MOVE.W D1,D0
        0x52, 0x40,             // ADDQ.W #1,D0
        0x3d, 0x40, 0xff, 0xfe, // MOVE.W D0,-2(A6)
        0x4e, 0x5e,             // UNLK A6
        0x52, 0x80,             // ADDQ.L #1,D0
        0x4e, 0x75,             // RTS
    ];
    if (!assembly.Success || !assembly.Bytes.SequenceEqual(expected))
    {
        foreach (var diagnostic in assembly.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine($"selftest failed: 機械語が期待値と異なります: {Convert.ToHexString(assembly.Bytes)}");
        return 1;
    }

    var addressingModule = new LanguageParser().Parse("""
        [addressing_demo
          move.l #$00ff0000,a0
          move.l #$00c00000,a1
          move.w #2,d1.w
        .loop:
          move.w (a0)+,d0.w
          move.w d0.w,(a1)+
          d1.w -= #1
          bne.s .loop
          lea table(pc),a2
          move.l #table,a3
          jmp (a2)
        table:
          dc.w $4e71
        ]/
        """);
    var addressingAssembly = new BinaryAssembler().Assemble(addressingModule.Module);
    byte[] addressingExpected =
    [
        0x20, 0x7c, 0x00, 0xff, 0x00, 0x00,
        0x22, 0x7c, 0x00, 0xc0, 0x00, 0x00,
        0x32, 0x3c, 0x00, 0x02,
        0x30, 0x18,
        0x32, 0xc0,
        0x53, 0x41,
        0x66, 0xf8,
        0x45, 0xfa, 0x00, 0x0a,
        0x26, 0x7c, 0x00, 0x00, 0x00, 0x24,
        0x4e, 0xd2,
        0x4e, 0x71,
    ];
    if (!addressingAssembly.Success
        || !addressingAssembly.Bytes.SequenceEqual(addressingExpected)
        || addressingAssembly.Symbols["addressing_demo.loop"] != 0x10
        || addressingAssembly.Symbols["table"] != 0x24)
    {
        foreach (var diagnostic in addressingAssembly.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine($"selftest failed: ラベル・分岐・実効アドレスの機械語が不正です: {Convert.ToHexString(addressingAssembly.Bytes)}");
        return 1;
    }

    var basedAssembly = new BinaryAssembler().Assemble(addressingModule.Module, 0x200);
    if (!basedAssembly.Success
        || basedAssembly.Symbols["addressing_demo"] != 0x200
        || basedAssembly.Symbols["table"] != 0x224
        || !basedAssembly.Bytes.AsSpan(30, 4).SequenceEqual(new byte[] { 0x00, 0x00, 0x02, 0x24 }))
    {
        Console.Error.WriteLine("selftest failed: ベースアドレスがシンボル参照へ反映されませんでした");
        return 1;
    }

    var modesModule = new LanguageParser().Parse("""
        [addressing_modes
          move.b d0.b,-(a0)
          move.w (a1),d1.w
          move.l 4(a2),d2.l
          move.w -8(a3,d3.w),d4.w
          move.l $1234.w,d5.l
          move.w $00c00000.l,d6.w
          move.l table(pc),d7.l
          move.w table(pc,d0.w),d0.w
          pea table(pc)
          jsr $00000100.l
          rts
        table:
          dc.l $12345678
        ]/
        """);
    var modesAssembly = new BinaryAssembler().Assemble(modesModule.Module);
    byte[] modesExpected =
    [
        0x11, 0x00,
        0x32, 0x11,
        0x24, 0x2a, 0x00, 0x04,
        0x38, 0x33, 0x30, 0xf8,
        0x2a, 0x38, 0x12, 0x34,
        0x3c, 0x39, 0x00, 0xc0, 0x00, 0x00,
        0x2e, 0x3a, 0x00, 0x12,
        0x30, 0x3b, 0x00, 0x0e,
        0x48, 0x7a, 0x00, 0x0a,
        0x4e, 0xb9, 0x00, 0x00, 0x01, 0x00,
        0x4e, 0x75,
        0x12, 0x34, 0x56, 0x78,
    ];
    if (!modesAssembly.Success || !modesAssembly.Bytes.SequenceEqual(modesExpected))
    {
        foreach (var diagnostic in modesAssembly.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine($"selftest failed: 実効アドレス形式の機械語が不正です: {Convert.ToHexString(modesAssembly.Bytes)}");
        return 1;
    }

    var logicalModule = new LanguageParser().Parse("""
        [raw_logical
          lsl.w #2,d1.w
          lsr.l #8,d0.l
          lsl.b d2.l,d3.b
          lsr.w d4.b,d5.w
          lsl.w (a0)
          lsr.w 4(a1)
          asr.w #8,d0.w
          asl.w #1,d1.w
          roxr.l d2.l,d3.l
          roxl.b d4.l,d5.b
          ror.w (a0)
          rol.w 4(a1)
          andi.b #$f0,d0.b
          andi.w #$1234,(a0)
          andi.l #$0000ffff,4(a1)
          or.b d0.b,d1.b
          or.w (a0),d2.w
          or.l d3.l,(a1)
          or.w #$00ff,d4.w
        ]/
        """);
    var logicalAssembly = new BinaryAssembler().Assemble(logicalModule.Module);
    byte[] logicalExpected =
    [
        0xe5, 0x49,
        0xe0, 0x88,
        0xe5, 0x2b,
        0xe8, 0x6d,
        0xe3, 0xd0,
        0xe2, 0xe9, 0x00, 0x04,
        0xe0, 0x40,
        0xe3, 0x41,
        0xe4, 0xb3,
        0xe9, 0x35,
        0xe6, 0xd0,
        0xe7, 0xe9, 0x00, 0x04,
        0x02, 0x00, 0x00, 0xf0,
        0x02, 0x50, 0x12, 0x34,
        0x02, 0xa9, 0x00, 0x00, 0xff, 0xff, 0x00, 0x04,
        0x82, 0x00,
        0x84, 0x50,
        0x87, 0x91,
        0x88, 0x7c, 0x00, 0xff,
    ];
    if (!logicalModule.Success
        || !logicalAssembly.Success
        || !logicalAssembly.Bytes.SequenceEqual(logicalExpected))
    {
        foreach (var diagnostic in logicalModule.Diagnostics.Concat(logicalAssembly.Diagnostics))
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine($"selftest failed: シフト・論理命令の機械語が不正です: {Convert.ToHexString(logicalAssembly.Bytes)}");
        return 1;
    }

    var invalidLogicalModule = new LanguageParser().Parse("""
        [invalid_logical
          lsl.w #0,d0.w
          asr.w #9,d0.w
          rol.b (a0)
          roxl.w a0
          andi.w d0.w,d1.w
          or.w (a0),(a1)
        ]/
        """);
    var invalidLogicalAssembly = new BinaryAssembler().Assemble(invalidLogicalModule.Module);
    if (invalidLogicalAssembly.Success || invalidLogicalAssembly.Diagnostics.Count != 6)
    {
        Console.Error.WriteLine("selftest failed: シフト・論理命令の不正オペランドを拒否できませんでした");
        return 1;
    }

    var forwardBranchModule = new LanguageParser().Parse("""
        [forward_branch
          bra.w .done
          nop
        .done:
          rts
        ]/
        """);
    var forwardBranchAssembly = new BinaryAssembler().Assemble(forwardBranchModule.Module);
    byte[] forwardBranchExpected = [0x60, 0x00, 0x00, 0x04, 0x4e, 0x71, 0x4e, 0x75];
    if (!forwardBranchAssembly.Success || !forwardBranchAssembly.Bytes.SequenceEqual(forwardBranchExpected))
    {
        Console.Error.WriteLine("selftest failed: ワード前方分岐の機械語が不正です");
        return 1;
    }

    var structuredModule = new LanguageParser().Parse("""
        defmacro SKIP_ON_NE(BODY) = {
          bne.s macro_end
          BODY
        macro_end:
        }

        struct base:a0 vector { x.l, y.l, z.l }

        [structured_demo
          move.l #$100,a0
          d0.l = vector,x
          vector,y = d0.l
          SKIP_ON_NE({
            SKIP_ON_NE({ nop })
            nop
          })
          d1.l = #(1 + 2 * 3)
          defb $aa, "B"
          align 4, $ff
          defw (1 << 2) + 3
        ]/
        """);
    var structuredAssembly = new BinaryAssembler().Assemble(structuredModule.Module);
    byte[] structuredExpected =
    [
        0x20, 0x7c, 0x00, 0x00, 0x01, 0x00,
        0x20, 0x28, 0x00, 0x00,
        0x21, 0x40, 0x00, 0x04,
        0x66, 0x06,
        0x66, 0x02,
        0x4e, 0x71,
        0x4e, 0x71,
        0x72, 0x07,
        0xaa, 0x42, 0xff, 0xff,
        0x00, 0x07,
    ];
    if (!structuredModule.Success
        || !structuredAssembly.Success
        || !structuredAssembly.Bytes.SequenceEqual(structuredExpected))
    {
        foreach (var diagnostic in structuredModule.Diagnostics.Concat(structuredAssembly.Diagnostics))
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine($"selftest failed: マクロ・スコープ・式・align・構造体の機械語が不正です: {Convert.ToHexString(structuredAssembly.Bytes)}");
        return 1;
    }

    var arithmeticModule = new LanguageParser().Parse("""
        [integer_arithmetic
          moveq #7,d0.l
          addq.w #8,d1.w
          subq.l #1,(a0)
          mulu.w d2.w,d0.l
          muls.w (a0),d1.l
          divu.w 4(a1),d2.l
          divs.w #-2,d3.l
          add.b d0.b,d1.b
          add.w 4(a1),d2.w
          add.l (a3)+,d3.l
          add.l #$12345678,d4.l
          cmp.b d0.b,d1.b
          cmp.w 4(a1),d2.w
          cmp.l (a3)+,d3.l
          cmp.l #$12345678,d4.l
        ]/
        """);
    var arithmeticAssembly = new BinaryAssembler().Assemble(arithmeticModule.Module);
    byte[] arithmeticExpected =
    [
        0x70, 0x07,
        0x50, 0x41,
        0x53, 0x90,
        0xc0, 0xc2,
        0xc3, 0xd0,
        0x84, 0xe9, 0x00, 0x04,
        0x87, 0xfc, 0xff, 0xfe,
        0xd2, 0x00,
        0xd4, 0x69, 0x00, 0x04,
        0xd6, 0x9b,
        0xd8, 0xbc, 0x12, 0x34, 0x56, 0x78,
        0xb2, 0x00,
        0xb4, 0x69, 0x00, 0x04,
        0xb6, 0x9b,
        0xb8, 0xbc, 0x12, 0x34, 0x56, 0x78,
    ];
    if (!arithmeticModule.Success
        || !arithmeticAssembly.Success
        || !arithmeticAssembly.Bytes.SequenceEqual(arithmeticExpected))
    {
        foreach (var diagnostic in arithmeticModule.Diagnostics.Concat(arithmeticAssembly.Diagnostics))
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine($"selftest failed: クイック／乗除算／RAW ADD・CMP命令の機械語が不正です: {Convert.ToHexString(arithmeticAssembly.Bytes)}");
        return 1;
    }

    var invalidArithmeticModule = new LanguageParser().Parse("""
        [invalid_arithmetic
          moveq.w #1,d0.l
          addq.w #0,d0.w
          mulu.l d0.l,d1.l
          mulu.w a0,d0.l
          divu.w d0.w,d1.w
          add.w a0,d0.w
          add.w (a0),(a1)
          cmp.w a0,d0.w
          cmp.w d0.w,(a0)
        ]/
        """);
    var invalidArithmeticAssembly = new BinaryAssembler().Assemble(invalidArithmeticModule.Module);
    if (invalidArithmeticAssembly.Success || invalidArithmeticAssembly.Diagnostics.Count != 9)
    {
        Console.Error.WriteLine("selftest failed: クイック／乗除算／RAW ADD・CMP命令の不正オペランドを拒否できませんでした");
        return 1;
    }

    var constantModule = new LanguageParser().Parse("""
        WORD_BYTES equ 2
        ROW_WORDS equ SCREEN_WIDTH / WORD_BYTES
        SCREEN_WIDTH equ 320
        ALIGNMENT equ WORD_BYTES * 4
        ALIGN_FILL equ $aa
        MASK equ (1 << 16) - 1
        ENABLED equ ROW_WORDS == 160

        [constant_expressions
          ifasm (ENABLED && MASK == $ffff) {
            d0.l = #MASK
          }
          align ALIGNMENT,ALIGN_FILL
          lsl.w #WORD_BYTES,d0.w
          andi.l #MASK,d0.l
          defw ROW_WORDS
          defl table + WORD_BYTES
        table:
          nop
        ]/
        """);
    var constantAssembly = new BinaryAssembler().Assemble(constantModule.Module);
    byte[] constantExpected =
    [
        0x20, 0x3c, 0x00, 0x00, 0xff, 0xff,
        0xaa, 0xaa,
        0xe5, 0x48,
        0x02, 0x80, 0x00, 0x00, 0xff, 0xff,
        0x00, 0xa0,
        0x00, 0x00, 0x00, 0x18,
        0x4e, 0x71,
    ];
    if (!constantModule.Success
        || constantModule.Module.Constants["ROW_WORDS"] != 160
        || constantModule.Module.Constants["MASK"] != 0xffff
        || !constantAssembly.Success
        || !constantAssembly.Bytes.SequenceEqual(constantExpected)
        || constantAssembly.Symbols["table"] != 0x16)
    {
        foreach (var diagnostic in constantModule.Diagnostics.Concat(constantAssembly.Diagnostics))
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.Error.WriteLine($"selftest failed: 定数ラベルまたは定数式の機械語が不正です: {Convert.ToHexString(constantAssembly.Bytes)}");
        return 1;
    }

    var scopedLabelsModule = new LanguageParser().Parse("""
        defmacro IDLE = { nop }

        [scoped_labels
          {
            bne.s same_name
            IDLE
          same_name:
          }
          {
            bne.s same_name
            nop
          same_name:
          }
        ]/
        """);
    var scopedLabelsAssembly = new BinaryAssembler().Assemble(scopedLabelsModule.Module);
    byte[] scopedLabelsExpected = [0x66, 0x02, 0x4e, 0x71, 0x66, 0x02, 0x4e, 0x71];
    if (!scopedLabelsModule.Success
        || !scopedLabelsAssembly.Success
        || !scopedLabelsAssembly.Bytes.SequenceEqual(scopedLabelsExpected))
    {
        Console.Error.WriteLine("selftest failed: 引数なしマクロまたは複文ラベルスコープが不正です");
        return 1;
    }

    var includeTestDirectory = Path.Combine(Path.GetTempPath(), $"m68kasm-selftest-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(includeTestDirectory);
        var includePath = Path.Combine(includeTestDirectory, "common.inc");
        var mainPath = Path.Combine(includeTestDirectory, "main.m68");
        File.WriteAllText(includePath, "INCLUDED_VALUE equ 7\ndefmacro INCLUDED = { nop }\n");
        const string includeSource = "include \"common.inc\"\n[included\nd0.w = #INCLUDED_VALUE\nINCLUDED\n]/\n";
        File.WriteAllText(mainPath, includeSource);
        var includeModule = new LanguageParser().Parse(includeSource, mainPath);
        var includeAssembly = new BinaryAssembler().Assemble(includeModule.Module);
        if (!includeModule.Success
            || includeModule.Module.Constants["INCLUDED_VALUE"] != 7
            || !includeAssembly.Success
            || !includeAssembly.Bytes.SequenceEqual(new byte[] { 0x30, 0x3c, 0x00, 0x07, 0x4e, 0x71 }))
        {
            Console.Error.WriteLine("selftest failed: 相対includeまたはinclude内マクロ／定数ラベルが不正です");
            return 1;
        }
    }
    finally
    {
        if (Directory.Exists(includeTestDirectory))
        {
            Directory.Delete(includeTestDirectory, recursive: true);
        }
    }

    var conditionalModule = new LanguageParser().Parse("""
        defmacro OPTION(FLAG) = {
          ifasm (FLAG) { nop } else { rts }
        }

        [conditional
          OPTION(0)
          ifasm (1 + 2 * 3 == 7) { nop }
        ]/
        """);
    var conditionalAssembly = new BinaryAssembler().Assemble(conditionalModule.Module);
    byte[] conditionalExpected = [0x4e, 0x75, 0x4e, 0x71];
    if (!conditionalModule.Success
        || !conditionalAssembly.Success
        || !conditionalAssembly.Bytes.SequenceEqual(conditionalExpected))
    {
        Console.Error.WriteLine("selftest failed: ifasm条件アセンブルが不正です");
        return 1;
    }

    var unresolvedModule = new LanguageParser().Parse("""
        [unresolved
          bra missing_label
        ]/
        """);
    var unresolvedAssembly = new BinaryAssembler().Assemble(unresolvedModule.Module);
    if (unresolvedAssembly.Success
        || unresolvedAssembly.Diagnostics.Count != 1
        || !unresolvedAssembly.Diagnostics[0].Message.Contains("未定義", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("selftest failed: 未定義ラベルをエラーにできませんでした");
        return 1;
    }

    var duplicateConstants = new LanguageParser().Parse("""
        VALUE equ 1
        value equ 2
        """);
    if (duplicateConstants.Success
        || !duplicateConstants.Diagnostics.Any(item => item.Message.Contains("すでに定義", StringComparison.Ordinal)))
    {
        Console.Error.WriteLine("selftest failed: 定数ラベルの重複を検出できませんでした");
        return 1;
    }

    var cyclicConstants = new LanguageParser().Parse("""
        FIRST equ SECOND + 1
        SECOND equ FIRST + 1
        """);
    if (cyclicConstants.Success
        || !cyclicConstants.Diagnostics.Any(item => item.Message.Contains("循環", StringComparison.Ordinal)))
    {
        Console.Error.WriteLine("selftest failed: 定数ラベルの循環参照を検出できませんでした");
        return 1;
    }

    var undefinedConstant = new LanguageParser().Parse("VALUE equ MISSING + 1");
    if (undefinedConstant.Success
        || !undefinedConstant.Diagnostics.Any(item => item.Message.Contains("未定義の定数ラベル", StringComparison.Ordinal)))
    {
        Console.Error.WriteLine("selftest failed: 定数ラベルの未定義参照を検出できませんでした");
        return 1;
    }

    var scopedConstant = new LanguageParser().Parse("""
        [invalid_constant_scope
          INNER equ 1
        ]/
        """);
    if (scopedConstant.Success
        || !scopedConstant.Diagnostics.Any(item => item.Message.Contains("トップレベル", StringComparison.Ordinal)))
    {
        Console.Error.WriteLine("selftest failed: 手続き内の定数ラベル定義を拒否できませんでした");
        return 1;
    }

    if (!InstructionSetProfileSelfTest.Run(out var instructionProfileFailure))
    {
        Console.Error.WriteLine($"selftest failed: {instructionProfileFailure}");
        return 1;
    }

    Console.WriteLine("selftest passed: syntax, macros/include/ifasm, constant/scoped labels, expressions, data/alignment, structures, branches, addressing, MC68000 instructions, and HAS060-verified MC68010/MC68020/MC68030 CPU profiles");
    return 0;
}
