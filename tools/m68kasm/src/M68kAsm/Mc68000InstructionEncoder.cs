using System.Globalization;
using System.Text.RegularExpressions;

namespace M68kAsm;

public sealed partial class BinaryAssembler
{
    private static readonly IReadOnlyDictionary<string, int> DbConditions =
        CreateConditionMnemonics("db", includeTrueFalse: true);

    private static readonly IReadOnlyDictionary<string, int> SetConditions =
        CreateConditionMnemonics("s", includeTrueFalse: true);

    private static readonly Regex GeneralRegister = new(
        @"^(?<kind>[dDaA])(?<register>[0-7])(?:\.(?<size>[bBwWlL]))?$",
        RegexOptions.Compiled);

    private static readonly Regex RegisterListItem = new(
        @"^(?<first>(?:[dD][0-7]|[aA][0-7]|[sS][pP]))(?:-(?<last>(?:[dD][0-7]|[aA][0-7]|[sS][pP])))?$",
        RegexOptions.Compiled);

    private static bool TryAssembleMc68000Instruction(
        string mnemonic,
        string suffix,
        string operandText,
        ProcedureSyntax procedure,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (DbConditions.TryGetValue(mnemonic, out var dbCondition))
        {
            AssembleDbCondition(dbCondition, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
            return true;
        }

        if (SetConditions.TryGetValue(mnemonic, out var setCondition))
        {
            AssembleSetCondition(setCondition, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
            return true;
        }

        switch (mnemonic)
        {
            case "abcd":
            case "sbcd":
                AssembleBcdAddOrSubtract(mnemonic == "abcd", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "add":
            case "sub":
            case "and":
            case "or":
                AssembleBinaryArithmeticOrLogical(mnemonic, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "adda":
            case "suba":
            case "cmpa":
                AssembleAddressArithmetic(mnemonic, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "addi":
                AssembleImmediateLogical(0x0600, "addi", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "subi":
                AssembleImmediateLogical(0x0400, "subi", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "andi":
                AssembleImmediateOrStatus(0x0200, 0x023c, 0x027c, "andi", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "ori":
                AssembleImmediateOrStatus(0x0000, 0x003c, 0x007c, "ori", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "eori":
                AssembleImmediateOrStatus(0x0a00, 0x0a3c, 0x0a7c, "eori", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "cmpi":
                AssembleImmediateLogical(0x0c00, "cmpi", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "addq":
            case "subq":
                AssembleQuickArithmetic(mnemonic == "addq", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "addx":
            case "subx":
                AssembleAddOrSubtractExtend(mnemonic == "addx", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "asl":
            case "asr":
            case "lsl":
            case "lsr":
            case "roxl":
            case "roxr":
            case "rol":
            case "ror":
                AssembleShiftOrRotate(mnemonic, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "bchg":
            case "bclr":
            case "bset":
            case "btst":
                AssembleBitOperation(mnemonic, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "chk":
                if (suffix == "l")
                {
                    if (RequireCpu(CpuModel.Mc68020, "CHK.L", context, statement, diagnostics))
                    {
                        AssembleLongCheck(operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                    }
                }
                else
                {
                    AssembleCheck(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "clr":
                AssembleUnaryDataInstruction(0x4200, "clr", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "cmp":
                AssembleRawDataToRegister(0xb000, "cmp", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "cmpm":
                AssembleCompareMemory(suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "mulu":
            case "muls":
            case "divu":
            case "divs":
                if (suffix == "l")
                {
                    if (RequireCpu(CpuModel.Mc68020, $"{mnemonic.ToUpperInvariant()}.L", context, statement, diagnostics))
                    {
                        AssembleLongMultiplyOrDivide(mnemonic, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                    }
                }
                else
                {
                    AssembleWordMultiplyOrDivide(mnemonic, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "eor":
                AssembleExclusiveOr(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "exg":
                AssembleExchange(suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "ext":
                AssembleExtend(suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "illegal":
                AssembleFixedInstruction(0x4afc, "illegal", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "jmp":
                AssembleSingleControlInstruction(0x4ec0, "jmp", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "jsr":
                AssembleSingleControlInstruction(0x4e80, "jsr", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "lea":
                AssembleLea(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "link":
                AssembleLink(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "move":
                if (!TryAssembleSpecialMove(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics))
                {
                    AssembleRawMove(false, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "movea":
                AssembleRawMove(true, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "movem":
                AssembleMoveMultiple(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "movep":
                AssembleMovePeripheral(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "moveq":
                AssembleMoveQuick(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "nbcd":
                AssembleByteUnaryInstruction(0x4800, "nbcd", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "neg":
                AssembleUnaryDataInstruction(0x4400, "neg", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "negx":
                AssembleUnaryDataInstruction(0x4000, "negx", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "nop":
                AssembleFixedInstruction(0x4e71, "nop", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "not":
                AssembleUnaryDataInstruction(0x4600, "not", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "pea":
                AssembleSingleControlInstruction(0x4840, "pea", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "reset":
                AssembleFixedInstruction(0x4e70, "reset", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "rte":
                AssembleFixedInstruction(0x4e73, "rte", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "rtr":
                AssembleFixedInstruction(0x4e77, "rtr", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "rts":
                AssembleFixedInstruction(0x4e75, "rts", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "stop":
                AssembleStop(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "swap":
                AssembleSwap(suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "tas":
                AssembleByteUnaryInstruction(0x4ac0, "tas", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "trap":
                AssembleTrap(suffix, operandText, procedure.Name, statement, writer, context, diagnostics);
                return true;
            case "trapv":
                AssembleFixedInstruction(0x4e76, "trapv", suffix, operandText, statement, writer, context, diagnostics);
                return true;
            case "tst":
                AssembleUnaryDataInstruction(0x4a00, "tst", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                return true;
            case "unlk":
                AssembleUnlink(suffix, operandText, statement, writer, context, diagnostics);
                return true;
            default:
                return TryAssemblePost68000Instruction(
                    mnemonic,
                    suffix,
                    operandText,
                    procedure,
                    layout,
                    statement,
                    writer,
                    context,
                    diagnostics);
        }
    }

    private static void AssembleBinaryArithmeticOrLogical(
        string mnemonic,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (!TryParseInstructionSize(suffix, out var size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}は2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], size, procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        var opcodeBase = mnemonic switch
        {
            "add" => 0xd000,
            "sub" => 0x9000,
            "and" => 0xc000,
            "or" => 0x8000,
            _ => throw new InvalidOperationException(),
        };

        if (destination.Value.Kind == EffectiveAddressKind.DataRegister)
        {
            if (!IsDataAddress(source.Value))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}のレジスタ出力形式ではソースにデータ実効アドレスを指定します");
                return;
            }

            var opcode = opcodeBase
                | (destination.Value.Register << 9)
                | SizeBits(size)
                | EncodeEffectiveAddress(source.Value);
            writer.WriteWord(checked((ushort)opcode));
            WriteEffectiveAddressExtensions(source.Value, size, procedureName, statement, writer, context, diagnostics);
            return;
        }

        if (source.Value.Kind != EffectiveAddressKind.DataRegister || !IsMemoryAlterable(destination.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}のメモリ出力形式はデータレジスタからメモリ可変実効アドレスへ指定します");
            return;
        }

        var memoryOpcode = opcodeBase
            | 0x0100
            | (source.Value.Register << 9)
            | SizeBits(size)
            | EncodeEffectiveAddress(destination.Value);
        writer.WriteWord(checked((ushort)memoryOpcode));
        WriteEffectiveAddressExtensions(destination.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleAddressArithmetic(
        string mnemonic,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix is not ("w" or "l"))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の幅は.wまたは.lです");
            return;
        }

        var size = suffix[0];
        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}は2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], 'l', procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (destination.Value.Kind != EffectiveAddressKind.AddressRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の出力先はアドレスレジスタです");
            return;
        }

        var opcodeBase = mnemonic switch
        {
            "adda" => size == 'w' ? 0xd0c0 : 0xd1c0,
            "suba" => size == 'w' ? 0x90c0 : 0x91c0,
            "cmpa" => size == 'w' ? 0xb0c0 : 0xb1c0,
            _ => throw new InvalidOperationException(),
        };
        writer.WriteWord((ushort)(opcodeBase
            | (destination.Value.Register << 9)
            | EncodeEffectiveAddress(source.Value)));
        WriteEffectiveAddressExtensions(source.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleImmediateOrStatus(
        int opcodeBase,
        int ccrOpcode,
        int srOpcode,
        string mnemonic,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var operands = SplitOperands(operandText);
        if (operands.Count == 2
            && (operands[1].Equals("ccr", StringComparison.OrdinalIgnoreCase)
                || operands[1].Equals("sr", StringComparison.OrdinalIgnoreCase)))
        {
            var toCcr = operands[1].Equals("ccr", StringComparison.OrdinalIgnoreCase);
            var size = toCcr ? 'b' : 'w';
            if (suffix.Length != 0 && suffix[0] != size)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}から{(toCcr ? "CCR" : "SR")}への幅は.{size}です");
                return;
            }

            var immediate = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
            if (immediate is null)
            {
                return;
            }

            if (immediate.Value.Kind != EffectiveAddressKind.Immediate)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の第1オペランドは即値です");
                return;
            }

            writer.WriteWord((ushort)(toCcr ? ccrOpcode : srOpcode));
            WriteEffectiveAddressExtensions(immediate.Value, size, procedureName, statement, writer, context, diagnostics);
            return;
        }

        AssembleImmediateLogical(opcodeBase, mnemonic, suffix, operandText, procedureName, layout, statement, writer, context, diagnostics);
    }

    private static void AssembleAddOrSubtractExtend(
        bool isAdd,
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var name = isAdd ? "addx" : "subx";
        if (!TryParseInstructionSize(suffix, out var size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は2つのオペランドを必要とします");
            return;
        }

        if (TryParseSizedDataRegister(operands[0], size, out var sourceRegister)
            && TryParseSizedDataRegister(operands[1], size, out var destinationRegister))
        {
            writer.WriteWord((ushort)((isAdd ? 0xd100 : 0x9100)
                | (destinationRegister << 9)
                | SizeBits(size)
                | sourceRegister));
            return;
        }

        if (TryParsePredecrementRegister(operands[0], out sourceRegister)
            && TryParsePredecrementRegister(operands[1], out destinationRegister))
        {
            writer.WriteWord((ushort)((isAdd ? 0xd108 : 0x9108)
                | (destinationRegister << 9)
                | SizeBits(size)
                | sourceRegister));
            return;
        }

        AddDiagnosticOnce(context, diagnostics, statement, $"{name}は同幅データレジスタ同士、またはプリデクリメントアドレス同士で指定します");
    }

    private static void AssembleExclusiveOr(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (!TryParseInstructionSize(suffix, out var size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "eorの幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "eorは2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], size, procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (source.Value.Kind != EffectiveAddressKind.DataRegister || !IsDataAlterable(destination.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "eorはデータレジスタからデータ可変実効アドレスへ指定します");
            return;
        }

        writer.WriteWord((ushort)(0xb100
            | (source.Value.Register << 9)
            | SizeBits(size)
            | EncodeEffectiveAddress(destination.Value)));
        WriteEffectiveAddressExtensions(destination.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleBcdAddOrSubtract(
        bool isAdd,
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var name = isAdd ? "abcd" : "sbcd";
        if (suffix.Length != 0 && suffix != "b")
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の幅は省略するか.bです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は2つのオペランドを必要とします");
            return;
        }

        if (TryParseOptionalSizedDataRegister(operands[0], 'b', out var source)
            && TryParseOptionalSizedDataRegister(operands[1], 'b', out var destination))
        {
            writer.WriteWord((ushort)((isAdd ? 0xc100 : 0x8100)
                | (destination << 9)
                | source));
            return;
        }

        if (TryParsePredecrementRegister(operands[0], out source)
            && TryParsePredecrementRegister(operands[1], out destination))
        {
            writer.WriteWord((ushort)((isAdd ? 0xc108 : 0x8108)
                | (destination << 9)
                | source));
            return;
        }

        AddDiagnosticOnce(context, diagnostics, statement, $"{name}はバイト幅データレジスタ同士、またはプリデクリメントアドレス同士で指定します");
    }

    private static void AssembleCheck(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "w")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "MC68000のchkは.wだけです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "chkは2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], 'w', procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], 'w', procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (!IsDataAddress(source.Value) || destination.Value.Kind != EffectiveAddressKind.DataRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "chkはワード幅データ実効アドレスとデータレジスタを指定します");
            return;
        }

        writer.WriteWord((ushort)(0x4180
            | (destination.Value.Register << 9)
            | EncodeEffectiveAddress(source.Value)));
        WriteEffectiveAddressExtensions(source.Value, 'w', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleCompareMemory(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (!TryParseInstructionSize(suffix, out var size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "cmpmの幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2
            || !TryParsePostincrementRegister(operands.ElementAtOrDefault(0) ?? string.Empty, out var source)
            || !TryParsePostincrementRegister(operands.ElementAtOrDefault(1) ?? string.Empty, out var destination))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "cmpmはポストインクリメントアドレス同士で指定します");
            return;
        }

        writer.WriteWord((ushort)(0xb108
            | (destination << 9)
            | SizeBits(size)
            | source));
    }

    private static void AssembleUnaryDataInstruction(
        int opcodeBase,
        string name,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (!TryParseInstructionSize(suffix, out var size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 1)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は1つのオペランドを必要とします");
            return;
        }

        var destination = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        if (destination is null)
        {
            return;
        }

        var upperCpuTest = name == "tst" && context.Cpu >= CpuModel.Mc68020;
        var legal = upperCpuTest
            ? IsDataAddress(destination.Value)
                || (destination.Value.Kind == EffectiveAddressKind.AddressRegister && size is 'w' or 'l')
            : IsDataAlterable(destination.Value);
        if (!legal)
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                upperCpuTest
                    ? "MC68020以降のtstにはデータ実効アドレス、または.w/.lのアドレスレジスタを指定します"
                    : $"{name}にはデータ可変実効アドレスを指定します");
            return;
        }

        writer.WriteWord((ushort)(opcodeBase | SizeBits(size) | EncodeEffectiveAddress(destination.Value)));
        WriteEffectiveAddressExtensions(destination.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleByteUnaryInstruction(
        int opcodeBase,
        string name,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "b")
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の幅は省略するか.bです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 1)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は1つのオペランドを必要とします");
            return;
        }

        var destination = ParseEffectiveAddress(operands[0], 'b', procedureName, layout, statement, context, diagnostics);
        if (destination is null)
        {
            return;
        }

        if (!IsDataAlterable(destination.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}にはデータ可変実効アドレスを指定します");
            return;
        }

        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(destination.Value)));
        WriteEffectiveAddressExtensions(destination.Value, 'b', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleBitOperation(
        string mnemonic,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix is not ("b" or "l"))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の幅は省略するか、メモリの.b／レジスタの.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}は2つのオペランドを必要とします");
            return;
        }

        var destinationIsRegister = RawDataRegister.IsMatch(operands[1]);
        var effectiveSize = destinationIsRegister ? 'l' : 'b';
        if (suffix.Length != 0 && suffix[0] != effectiveSize)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}のデータレジスタ形式は.l、メモリ形式は.bです");
            return;
        }

        var destination = ParseEffectiveAddress(operands[1], effectiveSize, procedureName, layout, statement, context, diagnostics);
        if (destination is null)
        {
            return;
        }

        var testOnly = mnemonic == "btst";
        if ((testOnly && !IsDataAddress(destination.Value))
            || (!testOnly && !IsDataAlterable(destination.Value)))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の出力先がMC68000で不正な実効アドレスです");
            return;
        }

        var operationBits = mnemonic switch
        {
            "btst" => 0x0000,
            "bchg" => 0x0040,
            "bclr" => 0x0080,
            "bset" => 0x00c0,
            _ => throw new InvalidOperationException(),
        };

        var bitSource = operands[0].Trim();
        if (bitSource.StartsWith('#'))
        {
            if (!TryEvaluateConstant(bitSource[1..], context.Constants, out var bitNumber, out var error))
            {
                AddDiagnosticOnce(context, diagnostics, statement, error);
                return;
            }

            var maximum = destinationIsRegister ? 31 : 7;
            if (bitNumber is < 0 || bitNumber > maximum)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の即値ビット番号は0から{maximum}です: {bitNumber}");
                return;
            }

            writer.WriteWord((ushort)(0x0800 | operationBits | EncodeEffectiveAddress(destination.Value)));
            writer.WriteWord((ushort)bitNumber);
        }
        else
        {
            if (!TryParseOptionalSizedDataRegister(bitSource, 'l', out var bitRegister))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}のビット番号は即値またはロング幅データレジスタです");
                return;
            }

            writer.WriteWord((ushort)(0x0100
                | (bitRegister << 9)
                | operationBits
                | EncodeEffectiveAddress(destination.Value)));
        }

        WriteEffectiveAddressExtensions(destination.Value, effectiveSize, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleDbCondition(
        int condition,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "w")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "DBccの幅は省略するか.wです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "DBccはデータレジスタと分岐先を必要とします");
            return;
        }

        if (!TryParseOptionalSizedDataRegister(operands[0], 'w', out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "DBccの第1オペランドはワード幅データレジスタです");
            return;
        }

        if (!IsValueExpression(operands[1]))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"DBccの分岐先を解釈できません: {operands[1]}");
            return;
        }

        var instructionPosition = writer.Position;
        writer.WriteWord((ushort)(0x50c8 | (condition << 8) | register));
        if (context.Pass == AssemblyPass.Layout)
        {
            if (!TryResolveExpression(operands[1], procedureName, statement, context, out _, out var layoutError))
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, layoutError));
            }

            writer.WriteWord(0);
            return;
        }

        if (!TryResolveExpression(operands[1], procedureName, statement, context, out var target, out var error))
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, error));
            writer.WriteWord(0);
            return;
        }

        var pc = (long)context.BaseAddress + instructionPosition + 2;
        WriteSignedDisplacement(target - pc, 16, statement, writer, diagnostics);
    }

    private static void AssembleSetCondition(
        int condition,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "b")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "Sccの幅は省略するか.bです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 1)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "Sccは1つのオペランドを必要とします");
            return;
        }

        var destination = ParseEffectiveAddress(operands[0], 'b', procedureName, layout, statement, context, diagnostics);
        if (destination is null)
        {
            return;
        }

        if (!IsDataAlterable(destination.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "Sccにはデータ可変実効アドレスを指定します");
            return;
        }

        writer.WriteWord((ushort)(0x50c0 | (condition << 8) | EncodeEffectiveAddress(destination.Value)));
        WriteEffectiveAddressExtensions(destination.Value, 'b', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleExchange(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "exgの幅は省略するか.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2
            || !TryParseGeneralRegister(operands.ElementAtOrDefault(0) ?? string.Empty, 'l', out var sourceIsAddress, out var source)
            || !TryParseGeneralRegister(operands.ElementAtOrDefault(1) ?? string.Empty, 'l', out var destinationIsAddress, out var destination))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "exgはロング幅データ／アドレスレジスタを2つ指定します");
            return;
        }

        int opcode;
        if (!sourceIsAddress && !destinationIsAddress)
        {
            opcode = 0xc140 | (source << 9) | destination;
        }
        else if (sourceIsAddress && destinationIsAddress)
        {
            opcode = 0xc148 | (source << 9) | destination;
        }
        else
        {
            var data = sourceIsAddress ? destination : source;
            var address = sourceIsAddress ? source : destination;
            opcode = 0xc188 | (data << 9) | address;
        }

        writer.WriteWord((ushort)opcode);
    }

    private static void AssembleExtend(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix is not ("w" or "l"))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "extの幅は.wまたは.lです");
            return;
        }

        if (!TryParseOptionalSizedDataRegister(operandText, suffix[0], out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "extのオペランドは命令幅と一致するデータレジスタです");
            return;
        }

        writer.WriteWord((ushort)((suffix == "w" ? 0x4880 : 0x48c0) | register));
    }

    private static bool TryAssembleSpecialMove(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            return false;
        }

        var sourceToken = operands[0].Trim();
        var destinationToken = operands[1].Trim();
        var sourceIsSr = sourceToken.Equals("sr", StringComparison.OrdinalIgnoreCase);
        var sourceIsCcr = sourceToken.Equals("ccr", StringComparison.OrdinalIgnoreCase);
        var sourceIsUsp = sourceToken.Equals("usp", StringComparison.OrdinalIgnoreCase);
        var destinationIsSr = destinationToken.Equals("sr", StringComparison.OrdinalIgnoreCase);
        var destinationIsCcr = destinationToken.Equals("ccr", StringComparison.OrdinalIgnoreCase);
        var destinationIsUsp = destinationToken.Equals("usp", StringComparison.OrdinalIgnoreCase);
        if (!(sourceIsSr || sourceIsCcr || sourceIsUsp || destinationIsSr || destinationIsCcr || destinationIsUsp))
        {
            return false;
        }

        if (sourceIsCcr)
        {
            if (!RequireCpu(CpuModel.Mc68010, "MOVE CCR,<ea>", context, statement, diagnostics))
            {
                return true;
            }

            AssembleMoveFromStatusRegister(0x42c0, "CCR", suffix, destinationToken, procedureName, layout, statement, writer, context, diagnostics);
            return true;
        }

        if (sourceIsSr)
        {
            AssembleMoveFromStatusRegister(0x40c0, "SR", suffix, destinationToken, procedureName, layout, statement, writer, context, diagnostics);
            return true;
        }

        if (destinationIsCcr || destinationIsSr)
        {
            AssembleMoveToStatusRegister(
                destinationIsCcr ? 0x44c0 : 0x46c0,
                destinationIsCcr ? "CCR" : "SR",
                'w',
                suffix,
                sourceToken,
                procedureName,
                layout,
                statement,
                writer,
                context,
                diagnostics);
            return true;
        }

        if (suffix.Length != 0 && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "MOVE USPの幅は省略するか.lです");
            return true;
        }

        if (sourceIsUsp && TryParseAddressRegisterToken(destinationToken, out var destinationAddress))
        {
            writer.WriteWord((ushort)(0x4e68 | destinationAddress));
            return true;
        }

        if (destinationIsUsp && TryParseAddressRegisterToken(sourceToken, out var sourceAddress))
        {
            writer.WriteWord((ushort)(0x4e60 | sourceAddress));
            return true;
        }

        AddDiagnosticOnce(context, diagnostics, statement, "MOVE USPはUSPとアドレスレジスタの間で指定します");
        return true;
    }

    private static void AssembleMoveFromStatusRegister(
        int opcodeBase,
        string registerName,
        string suffix,
        string destinationToken,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "w")
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"MOVE {registerName},<ea>の幅は省略するか.wです");
            return;
        }

        var destination = ParseEffectiveAddress(destinationToken, 'w', procedureName, layout, statement, context, diagnostics);
        if (destination is null)
        {
            return;
        }

        if (!IsDataAlterable(destination.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"MOVE {registerName},<ea>の出力先はデータ可変実効アドレスです");
            return;
        }

        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(destination.Value)));
        WriteEffectiveAddressExtensions(destination.Value, 'w', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleMoveToStatusRegister(
        int opcodeBase,
        string registerName,
        char size,
        string suffix,
        string sourceToken,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix[0] != size)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"MOVE <ea>,{registerName}の幅は.{size}です");
            return;
        }

        var source = ParseEffectiveAddress(sourceToken, size, procedureName, layout, statement, context, diagnostics);
        if (source is null)
        {
            return;
        }

        if (!IsDataAddress(source.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"MOVE <ea>,{registerName}のソースはデータ実効アドレスです");
            return;
        }

        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(source.Value)));
        WriteEffectiveAddressExtensions(source.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleMoveMultiple(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix is not ("w" or "l"))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movemの幅は.wまたは.lです");
            return;
        }

        var size = suffix[0];
        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movemはレジスタリストとメモリアドレスを必要とします");
            return;
        }

        var firstIsList = TryParseRegisterList(operands[0], out var firstMask, out var firstError);
        var secondIsList = TryParseRegisterList(operands[1], out var secondMask, out var secondError);
        if (firstIsList == secondIsList)
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                firstError.Length != 0 ? firstError : secondError.Length != 0 ? secondError : "movemの一方だけをレジスタリストにします");
            return;
        }

        var registersToMemory = firstIsList;
        var mask = firstIsList ? firstMask : secondMask;
        var addressToken = firstIsList ? operands[1] : operands[0];
        var address = ParseEffectiveAddress(addressToken, size, procedureName, layout, statement, context, diagnostics);
        if (address is null)
        {
            return;
        }

        var legal = registersToMemory
            ? address.Value.Kind is
                EffectiveAddressKind.Indirect
                or EffectiveAddressKind.PreDecrement
                or EffectiveAddressKind.Displacement
                or EffectiveAddressKind.Indexed
                or EffectiveAddressKind.AbsoluteWord
                or EffectiveAddressKind.AbsoluteLong
            : address.Value.Kind is
                EffectiveAddressKind.Indirect
                or EffectiveAddressKind.PostIncrement
                or EffectiveAddressKind.Displacement
                or EffectiveAddressKind.Indexed
                or EffectiveAddressKind.AbsoluteWord
                or EffectiveAddressKind.AbsoluteLong
                or EffectiveAddressKind.PcDisplacement
                or EffectiveAddressKind.PcIndexed;
        if (!legal)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"movemの{(registersToMemory ? "レジスタ→メモリ" : "メモリ→レジスタ")}形式で不正な実効アドレスです");
            return;
        }

        if (registersToMemory && address.Value.Kind == EffectiveAddressKind.PreDecrement)
        {
            mask = ReverseRegisterMask(mask);
        }

        var opcode = (registersToMemory ? 0x4880 : 0x4c80)
            | (size == 'l' ? 0x0040 : 0)
            | EncodeEffectiveAddress(address.Value);
        writer.WriteWord((ushort)opcode);
        writer.WriteWord(mask);
        WriteEffectiveAddressExtensions(address.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleMovePeripheral(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix is not ("w" or "l"))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movepの幅は.wまたは.lです");
            return;
        }

        var size = suffix[0];
        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movepは2つのオペランドを必要とします");
            return;
        }

        var sourceIsRegister = TryParseSizedDataRegister(operands[0], size, out var dataRegister);
        var destinationIsRegister = TryParseSizedDataRegister(operands[1], size, out var destinationRegister);
        if (sourceIsRegister == destinationIsRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movepはデータレジスタと16ビット変位付きアドレスの間で指定します");
            return;
        }

        dataRegister = sourceIsRegister ? dataRegister : destinationRegister;
        var memoryToken = sourceIsRegister ? operands[1] : operands[0];
        var memory = ParseEffectiveAddress(memoryToken, size, procedureName, layout, statement, context, diagnostics);
        if (memory is null)
        {
            return;
        }

        if (memory.Value.Kind != EffectiveAddressKind.Displacement)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movepのメモリオペランドは16ビット変位付きアドレスレジスタです");
            return;
        }

        var opcodeBase = sourceIsRegister
            ? size == 'w' ? 0x0188 : 0x01c8
            : size == 'w' ? 0x0108 : 0x0148;
        writer.WriteWord((ushort)(opcodeBase
            | (dataRegister << 9)
            | memory.Value.Register));
        WriteEffectiveAddressExtensions(memory.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleLink(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "w" && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "linkの幅は.wまたは.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2 || !TryParseAddressRegisterToken(operands.ElementAtOrDefault(0) ?? string.Empty, out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "linkはアドレスレジスタと即値を必要とします");
            return;
        }

        var size = suffix == "l" ? 'l' : 'w';
        if (size == 'l' && !RequireCpu(CpuModel.Mc68020, "LINK.L", context, statement, diagnostics))
        {
            return;
        }

        var immediate = ParseImmediateOperand(operands[1], size, procedureName, layout, statement, context, diagnostics);
        if (immediate is null)
        {
            return;
        }

        writer.WriteWord((ushort)((size == 'l' ? 0x4808 : 0x4e50) | register));
        WriteEffectiveAddressExtensions(immediate.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleStop(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "w")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "stopの幅は省略するか.wです");
            return;
        }

        var immediate = ParseImmediateOperand(operandText, 'w', procedureName, layout, statement, context, diagnostics);
        if (immediate is null)
        {
            return;
        }

        writer.WriteWord(0x4e72);
        WriteEffectiveAddressExtensions(immediate.Value, 'w', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleSwap(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "swapの幅は省略するか.lです");
            return;
        }

        if (!TryParseOptionalSizedDataRegister(operandText, 'l', out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "swapのオペランドはロング幅データレジスタです");
            return;
        }

        writer.WriteWord((ushort)(0x4840 | register));
    }

    private static void AssembleTrap(
        string suffix,
        string operandText,
        string procedureName,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "trapに幅接尾辞は指定しません");
            return;
        }

        var token = operandText.Trim();
        if (!token.StartsWith('#'))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "trapは#0から#15のベクタを指定します");
            return;
        }

        if (!TryResolveExpression(token[1..], procedureName, statement, context, out var vector, out var error))
        {
            AddDiagnosticOnce(context, diagnostics, statement, error);
            return;
        }

        if (vector is < 0 or > 15)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"trapのベクタは0から15です: {vector}");
            return;
        }

        writer.WriteWord((ushort)(0x4e40 | vector));
    }

    private static void AssembleUnlink(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "unlkに幅接尾辞は指定しません");
            return;
        }

        if (!TryParseAddressRegisterToken(operandText, out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "unlkのオペランドはアドレスレジスタです");
            return;
        }

        writer.WriteWord((ushort)(0x4e58 | register));
    }

    private static void AssembleFixedInstruction(
        ushort opcode,
        string name,
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 || operandText.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}に幅接尾辞やオペランドは指定しません");
            return;
        }

        writer.WriteWord(opcode);
    }

    private static EffectiveAddress? ParseImmediateOperand(
        string token,
        char size,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var immediate = ParseEffectiveAddress(token, size, procedureName, layout, statement, context, diagnostics);
        if (immediate is not null && immediate.Value.Kind != EffectiveAddressKind.Immediate)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "即値オペランドが必要です");
            return null;
        }

        return immediate;
    }

    private static bool RequireCpu(
        CpuModel minimum,
        string instruction,
        AssemblyContext context,
        StatementSyntax statement,
        List<SourceDiagnostic> diagnostics)
    {
        if (context.Cpu >= minimum)
        {
            return true;
        }

        AddDiagnosticOnce(
            context,
            diagnostics,
            statement,
            $"{instruction}はMC{minimum.ToCommandLineName()}以降の命令です（現在: --cpu {context.Cpu.ToCommandLineName()}）");
        return false;
    }

    private static bool TryParseGeneralRegister(
        string token,
        char impliedSize,
        out bool isAddress,
        out int register)
    {
        isAddress = false;
        register = 0;
        var match = GeneralRegister.Match(token.Trim());
        if (!match.Success)
        {
            if (TryParseAddressRegisterToken(token, out register))
            {
                isAddress = true;
                return true;
            }

            return false;
        }

        if (match.Groups["size"].Success
            && char.ToLowerInvariant(match.Groups["size"].Value[0]) != impliedSize)
        {
            return false;
        }

        isAddress = char.ToLowerInvariant(match.Groups["kind"].Value[0]) == 'a';
        register = match.Groups["register"].Value[0] - '0';
        return true;
    }

    private static bool TryParseSizedDataRegister(string token, char size, out int register)
    {
        register = 0;
        var match = RawDataRegister.Match(token.Trim());
        if (!match.Success
            || !match.Groups["size"].Success
            || char.ToLowerInvariant(match.Groups["size"].Value[0]) != size)
        {
            return false;
        }

        register = match.Groups["register"].Value[0] - '0';
        return true;
    }

    private static bool TryParseOptionalSizedDataRegister(string token, char size, out int register)
    {
        register = 0;
        var match = RawDataRegister.Match(token.Trim());
        if (!match.Success
            || (match.Groups["size"].Success
                && char.ToLowerInvariant(match.Groups["size"].Value[0]) != size))
        {
            return false;
        }

        register = match.Groups["register"].Value[0] - '0';
        return true;
    }

    private static bool TryParseAddressRegisterToken(string token, out int register)
    {
        register = 0;
        var match = AddressRegister.Match(token.Trim());
        if (!match.Success)
        {
            return false;
        }

        register = ParseAddressRegister(match.Value);
        return true;
    }

    private static bool TryParsePredecrementRegister(string token, out int register)
    {
        register = 0;
        var match = PreDecrement.Match(token.Trim());
        if (!match.Success)
        {
            return false;
        }

        register = ParseAddressRegister(match.Groups["register"].Value);
        return true;
    }

    private static bool TryParsePostincrementRegister(string token, out int register)
    {
        register = 0;
        var match = PostIncrement.Match(token.Trim());
        if (!match.Success)
        {
            return false;
        }

        register = ParseAddressRegister(match.Groups["register"].Value);
        return true;
    }

    private static bool TryParseRegisterList(string text, out ushort mask, out string error)
    {
        mask = 0;
        error = string.Empty;
        var items = text.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0)
        {
            error = "空のレジスタリストです";
            return false;
        }

        foreach (var item in items)
        {
            var match = RegisterListItem.Match(item);
            if (!match.Success)
            {
                error = $"レジスタリストを解釈できません: {item}";
                return false;
            }

            var first = RegisterListIndex(match.Groups["first"].Value);
            var last = match.Groups["last"].Success
                ? RegisterListIndex(match.Groups["last"].Value)
                : first;
            if ((first < 8) != (last < 8) || last < first)
            {
                error = $"レジスタ範囲は同種で昇順に指定します: {item}";
                return false;
            }

            for (var index = first; index <= last; index++)
            {
                var bit = (ushort)(1 << index);
                if ((mask & bit) != 0)
                {
                    error = $"レジスタリストで重複しています: {item}";
                    return false;
                }

                mask |= bit;
            }
        }

        return true;
    }

    private static int RegisterListIndex(string token)
    {
        var normalized = token.Trim().ToLowerInvariant();
        if (normalized == "sp")
        {
            return 15;
        }

        var register = normalized[1] - '0';
        return normalized[0] == 'd' ? register : register + 8;
    }

    private static ushort ReverseRegisterMask(ushort mask)
    {
        ushort reversed = 0;
        for (var index = 0; index < 16; index++)
        {
            if ((mask & (1 << index)) != 0)
            {
                reversed |= (ushort)(1 << (15 - index));
            }
        }

        return reversed;
    }

    private static IReadOnlyDictionary<string, int> CreateConditionMnemonics(
        string prefix,
        bool includeTrueFalse)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (includeTrueFalse)
        {
            result[prefix + "t"] = 0x0;
            result[prefix + "f"] = 0x1;
        }

        result[prefix + "hi"] = 0x2;
        result[prefix + "ls"] = 0x3;
        result[prefix + "cc"] = 0x4;
        result[prefix + "hs"] = 0x4;
        result[prefix + "cs"] = 0x5;
        result[prefix + "lo"] = 0x5;
        result[prefix + "ne"] = 0x6;
        result[prefix + "eq"] = 0x7;
        result[prefix + "vc"] = 0x8;
        result[prefix + "vs"] = 0x9;
        result[prefix + "pl"] = 0xa;
        result[prefix + "mi"] = 0xb;
        result[prefix + "ge"] = 0xc;
        result[prefix + "lt"] = 0xd;
        result[prefix + "gt"] = 0xe;
        result[prefix + "le"] = 0xf;
        if (prefix.Equals("db", StringComparison.OrdinalIgnoreCase))
        {
            result["dbra"] = 0x1;
        }

        return result;
    }
}
