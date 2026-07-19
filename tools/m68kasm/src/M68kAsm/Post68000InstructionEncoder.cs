using System.Text.RegularExpressions;

namespace M68kAsm;

public sealed partial class BinaryAssembler
{
    private static readonly IReadOnlyDictionary<string, int> ControlRegisters =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["sfc"] = 0x000,
            ["dfc"] = 0x001,
            ["cacr"] = 0x002,
            ["usp"] = 0x800,
            ["vbr"] = 0x801,
            ["caar"] = 0x802,
            ["msp"] = 0x803,
            ["isp"] = 0x804,
        };

    private static readonly IReadOnlyDictionary<string, int> TrapConditions =
        CreateConditionMnemonics("trap", includeTrueFalse: true);

    private static readonly Regex BitFieldOperand = new(
        @"^(?<ea>.+)\{\s*(?<offset>[^:]+)\s*:\s*(?<width>[^}]+)\s*\}$",
        RegexOptions.Compiled);

    private static readonly Regex DataRegisterPair = new(
        @"^(?<high>[dD][0-7](?:\.[wWlL])?):(?<low>[dD][0-7](?:\.[wWlL])?)$",
        RegexOptions.Compiled);

    private static readonly Regex Cas2AddressPair = new(
        @"^\((?<first>[dDaA][0-7])\):\((?<second>[dDaA][0-7])\)$",
        RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, MmuRegisterInfo> MmuRegisters =
        new Dictionary<string, MmuRegisterInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["tc"] = new(0, MmuRegisterFormat.RootOrControl, 'l'),
            ["srp"] = new(2, MmuRegisterFormat.RootOrControl, 'q'),
            ["crp"] = new(3, MmuRegisterFormat.RootOrControl, 'q'),
            ["tt0"] = new(2, MmuRegisterFormat.TransparentTranslation, 'l'),
            ["tt1"] = new(3, MmuRegisterFormat.TransparentTranslation, 'l'),
            ["mmusr"] = new(0, MmuRegisterFormat.Status, 'w'),
        };

    private static bool TryAssemblePost68000Instruction(
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
        if (TrapConditions.TryGetValue(mnemonic, out var trapCondition))
        {
            if (RequireCpu(CpuModel.Mc68020, "TRAPcc", context, statement, diagnostics))
            {
                AssembleTrapCondition(trapCondition, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
            }
            return true;
        }

        switch (mnemonic)
        {
            case "bkpt":
                if (RequireCpu(CpuModel.Mc68010, "BKPT", context, statement, diagnostics))
                {
                    AssembleBreakpoint(suffix, operandText, procedure.Name, statement, writer, context, diagnostics);
                }
                return true;
            case "movec":
                if (RequireCpu(CpuModel.Mc68010, "MOVEC", context, statement, diagnostics))
                {
                    AssembleMoveControlRegister(suffix, operandText, statement, writer, context, diagnostics);
                }
                return true;
            case "moves":
                if (RequireCpu(CpuModel.Mc68010, "MOVES", context, statement, diagnostics))
                {
                    AssembleMoveAlternateSpace(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "rtd":
                if (RequireCpu(CpuModel.Mc68010, "RTD", context, statement, diagnostics))
                {
                    AssembleReturnAndDeallocate(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "bfchg":
            case "bfclr":
            case "bfexts":
            case "bfextu":
            case "bfffo":
            case "bfins":
            case "bfset":
            case "bftst":
                if (RequireCpu(CpuModel.Mc68020, mnemonic.ToUpperInvariant(), context, statement, diagnostics))
                {
                    AssembleBitField(mnemonic, suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "callm":
                if (RequireOnlyMc68020("CALLM", context, statement, diagnostics))
                {
                    AssembleCallModule(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "cas":
                if (RequireCpu(CpuModel.Mc68020, "CAS", context, statement, diagnostics))
                {
                    AssembleCompareAndSwap(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "cas2":
                if (RequireCpu(CpuModel.Mc68020, "CAS2", context, statement, diagnostics))
                {
                    AssembleCompareAndSwapDual(suffix, operandText, statement, writer, context, diagnostics);
                }
                return true;
            case "chk2":
            case "cmp2":
                if (RequireCpu(CpuModel.Mc68020, mnemonic.ToUpperInvariant(), context, statement, diagnostics))
                {
                    AssembleCheckOrCompareBounds(mnemonic == "chk2", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "divsl":
            case "divul":
                if (RequireCpu(CpuModel.Mc68020, mnemonic.ToUpperInvariant(), context, statement, diagnostics))
                {
                    if (suffix != "l")
                    {
                        AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の幅は.lです");
                    }
                    else
                    {
                        AssembleLongMultiplyOrDivide(mnemonic, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                    }
                }
                return true;
            case "extb":
                if (RequireCpu(CpuModel.Mc68020, "EXTB", context, statement, diagnostics))
                {
                    AssembleExtendByte(suffix, operandText, statement, writer, context, diagnostics);
                }
                return true;
            case "pack":
            case "unpk":
                if (RequireCpu(CpuModel.Mc68020, mnemonic.ToUpperInvariant(), context, statement, diagnostics))
                {
                    AssemblePackOrUnpack(mnemonic == "pack", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "rtm":
                if (RequireOnlyMc68020("RTM", context, statement, diagnostics))
                {
                    AssembleReturnFromModule(suffix, operandText, statement, writer, context, diagnostics);
                }
                return true;
            case "pflusha":
                if (RequireOnlyMc68030("PFLUSHA", context, statement, diagnostics))
                {
                    AssembleFlushAll(suffix, operandText, statement, writer, context, diagnostics);
                }
                return true;
            case "pflush":
                if (RequireOnlyMc68030("PFLUSH", context, statement, diagnostics))
                {
                    AssembleFlushMmu(suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "ploadr":
            case "ploadw":
                if (RequireOnlyMc68030(mnemonic.ToUpperInvariant(), context, statement, diagnostics))
                {
                    AssembleLoadMmu(mnemonic == "ploadr", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "pmove":
            case "pmovefd":
                if (RequireOnlyMc68030(mnemonic.ToUpperInvariant(), context, statement, diagnostics))
                {
                    AssembleMoveMmu(mnemonic == "pmovefd", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            case "ptestr":
            case "ptestw":
                if (RequireOnlyMc68030(mnemonic.ToUpperInvariant(), context, statement, diagnostics))
                {
                    AssembleTestMmu(mnemonic == "ptestr", suffix, operandText, procedure.Name, layout, statement, writer, context, diagnostics);
                }
                return true;
            default:
                return false;
        }
    }

    private static void AssembleBreakpoint(
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
            AddDiagnosticOnce(context, diagnostics, statement, "bkptに幅接尾辞は指定しません");
            return;
        }

        var token = operandText.Trim();
        if (!token.StartsWith('#'))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "bkptは#0から#7のベクタを指定します");
            return;
        }

        if (!TryResolveExpression(token[1..], procedureName, statement, context, out var vector, out var error))
        {
            AddDiagnosticOnce(context, diagnostics, statement, error);
            return;
        }

        if (vector is < 0 or > 7)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"bkptのベクタは0から7です: {vector}");
            return;
        }

        writer.WriteWord((ushort)(0x4848 | vector));
    }

    private static void AssembleMoveControlRegister(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movecの幅は省略するか.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movecは汎用レジスタと制御レジスタを必要とします");
            return;
        }

        var sourceIsControl = ControlRegisters.TryGetValue(operands[0], out var sourceControl);
        var destinationIsControl = ControlRegisters.TryGetValue(operands[1], out var destinationControl);
        if (sourceIsControl == destinationIsControl)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movecの一方だけを制御レジスタにします");
            return;
        }

        var generalToken = sourceIsControl ? operands[1] : operands[0];
        if (!TryParseGeneralRegister(generalToken, 'l', out var isAddress, out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movecの汎用オペランドはロング幅データ／アドレスレジスタです");
            return;
        }

        var controlName = sourceIsControl ? operands[0] : operands[1];
        var minimumCpu = controlName.ToLowerInvariant() switch
        {
            "sfc" or "dfc" or "usp" or "vbr" => CpuModel.Mc68010,
            "cacr" or "caar" or "msp" or "isp" => CpuModel.Mc68020,
            _ => CpuModel.Mc68010,
        };
        if (!RequireCpu(minimumCpu, $"MOVEC {controlName}", context, statement, diagnostics))
        {
            return;
        }

        var control = sourceIsControl ? sourceControl : destinationControl;
        var extension = (isAddress ? 0x8000 : 0)
            | (register << 12)
            | control;
        writer.WriteWord((ushort)(sourceIsControl ? 0x4e7a : 0x4e7b));
        writer.WriteWord((ushort)extension);
    }

    private static void AssembleMoveAlternateSpace(
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
            AddDiagnosticOnce(context, diagnostics, statement, "movesの幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movesは汎用レジスタとメモリ可変実効アドレスを必要とします");
            return;
        }

        var sourceIsRegister = TryParseGeneralRegister(operands[0], size, out var sourceIsAddress, out var sourceRegister);
        var destinationIsRegister = TryParseGeneralRegister(operands[1], size, out var destinationIsAddress, out var destinationRegister);
        if (sourceIsRegister == destinationIsRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movesの一方だけを汎用レジスタにします");
            return;
        }

        var registerIsAddress = sourceIsRegister ? sourceIsAddress : destinationIsAddress;
        var register = sourceIsRegister ? sourceRegister : destinationRegister;
        if (registerIsAddress && size == 'b')
        {
            AddDiagnosticOnce(context, diagnostics, statement, "moves.bにアドレスレジスタは使えません");
            return;
        }

        var memoryToken = sourceIsRegister ? operands[1] : operands[0];
        var memory = ParseEffectiveAddress(memoryToken, size, procedureName, layout, statement, context, diagnostics);
        if (memory is null)
        {
            return;
        }

        if (!IsMemoryAlterable(memory.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "movesのメモリオペランドはメモリ可変実効アドレスです");
            return;
        }

        writer.WriteWord((ushort)(0x0e00 | SizeBits(size) | EncodeEffectiveAddress(memory.Value)));
        writer.WriteWord((ushort)((registerIsAddress ? 0x8000 : 0)
            | (register << 12)
            | (sourceIsRegister ? 0x0800 : 0)));
        WriteEffectiveAddressExtensions(memory.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleReturnAndDeallocate(
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
            AddDiagnosticOnce(context, diagnostics, statement, "rtdの幅は省略するか.wです");
            return;
        }

        var immediate = ParseImmediateOperand(operandText, 'w', procedureName, layout, statement, context, diagnostics);
        if (immediate is null)
        {
            return;
        }

        writer.WriteWord(0x4e74);
        WriteEffectiveAddressExtensions(immediate.Value, 'w', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleLongCheck(
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
            AddDiagnosticOnce(context, diagnostics, statement, "chk.lは2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], 'l', procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], 'l', procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (!IsDataAddress(source.Value) || destination.Value.Kind != EffectiveAddressKind.DataRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "chk.lはロング幅データ実効アドレスとデータレジスタを指定します");
            return;
        }

        writer.WriteWord((ushort)(0x4100
            | (destination.Value.Register << 9)
            | EncodeEffectiveAddress(source.Value)));
        WriteEffectiveAddressExtensions(source.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleLongMultiplyOrDivide(
        string mnemonic,
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
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}.lは2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], 'l', procedureName, layout, statement, context, diagnostics);
        if (source is null)
        {
            return;
        }

        if (!IsDataAddress(source.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}.lのソースはデータ実効アドレスです");
            return;
        }

        var signed = mnemonic is "muls" or "divs" or "divsl";
        var divide = mnemonic.StartsWith("div", StringComparison.Ordinal);
        var requiresPair = mnemonic is "divsl" or "divul";
        int high;
        int low;
        var pair = TryParseDataRegisterPair(operands[1], 'l', out high, out low);
        if (!pair)
        {
            if (requiresPair || !TryParseOptionalSizedDataRegister(operands[1], 'l', out low))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}.lの出力先はロング幅データレジスタ、または上位:下位レジスタ対です");
                return;
            }

            high = low;
        }

        var opcodeBase = divide ? 0x4c40 : 0x4c00;
        var extension = (low << 12)
            | (signed ? 0x0800 : 0)
            | (!divide && pair ? 0x0400 : 0)
            | high;
        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(source.Value)));
        writer.WriteWord((ushort)extension);
        WriteEffectiveAddressExtensions(source.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleBitField(
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
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}に幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        var insert = mnemonic == "bfins";
        var hasDestinationRegister = mnemonic is "bfexts" or "bfextu" or "bfffo";
        var expectedCount = insert || hasDestinationRegister ? 2 : 1;
        if (operands.Count != expectedCount)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}のオペランド数が不正です");
            return;
        }

        var fieldToken = insert ? operands[1] : operands[0];
        var field = BitFieldOperand.Match(fieldToken.Trim());
        if (!field.Success)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}のビットフィールドは<ea>{{offset:width}}で指定します");
            return;
        }

        var eaToken = field.Groups["ea"].Value.Trim();
        if (RawDataRegister.Match(eaToken) is { Success: true } dataMatch
            && !dataMatch.Groups["size"].Success)
        {
            eaToken += ".l";
        }

        var address = ParseEffectiveAddress(eaToken, 'l', procedureName, layout, statement, context, diagnostics);
        if (address is null)
        {
            return;
        }

        var modifies = mnemonic is "bfchg" or "bfclr" or "bfins" or "bfset";
        var legal = address.Value.Kind == EffectiveAddressKind.DataRegister
            || (modifies ? IsAlterableControlAddress(address.Value) : IsControlAddress(address.Value));
        if (!legal)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}で不正な実効アドレスです");
            return;
        }

        var registerField = 0;
        if (insert)
        {
            if (!TryParseOptionalSizedDataRegister(operands[0], 'l', out registerField))
            {
                AddDiagnosticOnce(context, diagnostics, statement, "bfinsの第1オペランドはロング幅データレジスタです");
                return;
            }
        }
        else if (hasDestinationRegister)
        {
            if (!TryParseOptionalSizedDataRegister(operands[1], 'l', out registerField))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の出力先はロング幅データレジスタです");
                return;
            }
        }

        if (!TryEncodeBitFieldValue(field.Groups["offset"].Value, true, context, statement, diagnostics, out var offsetField)
            || !TryEncodeBitFieldValue(field.Groups["width"].Value, false, context, statement, diagnostics, out var widthField))
        {
            return;
        }

        var opcodeBase = mnemonic switch
        {
            "bftst" => 0xe8c0,
            "bfextu" => 0xe9c0,
            "bfchg" => 0xeac0,
            "bfexts" => 0xebc0,
            "bfclr" => 0xecc0,
            "bfffo" => 0xedc0,
            "bfset" => 0xeec0,
            "bfins" => 0xefc0,
            _ => throw new InvalidOperationException(),
        };
        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(address.Value)));
        writer.WriteWord((ushort)((registerField << 12) | offsetField | widthField));
        WriteEffectiveAddressExtensions(address.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static bool TryEncodeBitFieldValue(
        string token,
        bool offset,
        AssemblyContext context,
        RawStatement statement,
        List<SourceDiagnostic> diagnostics,
        out int encoded)
    {
        encoded = 0;
        var valueToken = token.Trim();
        if (TryParseOptionalSizedDataRegister(valueToken, 'l', out var register))
        {
            encoded = (offset ? 0x0800 | (register << 6) : 0x0020 | register);
            return true;
        }

        valueToken = valueToken.TrimStart('#');
        if (!TryEvaluateConstant(valueToken, context.Constants, out var value, out var error))
        {
            AddDiagnosticOnce(context, diagnostics, statement, error);
            return false;
        }

        if (offset)
        {
            if (value is < 0 or > 31)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"ビットフィールドオフセットは0から31です: {value}");
                return false;
            }

            encoded = checked((int)value) << 6;
            return true;
        }

        if (value is < 1 or > 32)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"ビットフィールド幅は1から32です: {value}");
            return false;
        }

        encoded = value == 32 ? 0 : checked((int)value);
        return true;
    }

    private static void AssembleCallModule(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "callmに幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "callmはモジュール引数即値と制御アドレスを必要とします");
            return;
        }

        var immediate = ParseImmediateOperand(operands[0], 'b', procedureName, layout, statement, context, diagnostics);
        var address = ParseEffectiveAddress(operands[1], 'l', procedureName, layout, statement, context, diagnostics);
        if (immediate is null || address is null)
        {
            return;
        }

        if (!IsControlAddress(address.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "callmの第2オペランドは制御アドレスです");
            return;
        }

        writer.WriteWord((ushort)(0x06c0 | EncodeEffectiveAddress(address.Value)));
        WriteEffectiveAddressExtensions(immediate.Value, 'b', procedureName, statement, writer, context, diagnostics);
        WriteEffectiveAddressExtensions(address.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleCompareAndSwap(
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
            AddDiagnosticOnce(context, diagnostics, statement, "casの幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 3
            || !TryParseOptionalSizedDataRegister(operands.ElementAtOrDefault(0) ?? string.Empty, size, out var compare)
            || !TryParseOptionalSizedDataRegister(operands.ElementAtOrDefault(1) ?? string.Empty, size, out var update))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "casは比較レジスタ、更新レジスタ、メモリ可変実効アドレスを指定します");
            return;
        }

        var address = ParseEffectiveAddress(operands[2], size, procedureName, layout, statement, context, diagnostics);
        if (address is null)
        {
            return;
        }

        if (!IsMemoryAlterable(address.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "casの第3オペランドはメモリ可変実効アドレスです");
            return;
        }

        var opcodeBase = size switch
        {
            'b' => 0x0ac0,
            'w' => 0x0cc0,
            'l' => 0x0ec0,
            _ => throw new InvalidOperationException(),
        };
        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(address.Value)));
        writer.WriteWord((ushort)((update << 6) | compare));
        WriteEffectiveAddressExtensions(address.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleCompareAndSwapDual(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix is not ("w" or "l"))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "cas2の幅は.wまたは.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 3
            || !TryParseDataRegisterPair(operands.ElementAtOrDefault(0) ?? string.Empty, suffix[0], out var compare1, out var compare2)
            || !TryParseDataRegisterPair(operands.ElementAtOrDefault(1) ?? string.Empty, suffix[0], out var update1, out var update2)
            || !TryParseCas2AddressPair(operands.ElementAtOrDefault(2) ?? string.Empty, out var address1IsAddress, out var address1, out var address2IsAddress, out var address2))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "cas2はDc1:Dc2,Du1:Du2,(Rn1):(Rn2)で指定します");
            return;
        }

        writer.WriteWord((ushort)(suffix == "w" ? 0x0cfc : 0x0efc));
        writer.WriteWord((ushort)((address1IsAddress ? 0x8000 : 0)
            | (address1 << 12)
            | (update1 << 6)
            | compare1));
        writer.WriteWord((ushort)((address2IsAddress ? 0x8000 : 0)
            | (address2 << 12)
            | (update2 << 6)
            | compare2));
    }

    private static void AssembleCheckOrCompareBounds(
        bool trapOnFailure,
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
            AddDiagnosticOnce(context, diagnostics, statement, $"{(trapOnFailure ? "chk2" : "cmp2")}の幅は.b、.w、.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "chk2/cmp2は境界メモリと汎用レジスタを必要とします");
            return;
        }

        var bounds = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        if (bounds is null)
        {
            return;
        }

        if (!IsControlAddress(bounds.Value)
            || !TryParseGeneralRegister(operands[1], size, out var isAddress, out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "chk2/cmp2は制御アドレスと同幅の汎用レジスタを指定します");
            return;
        }

        var opcodeBase = size switch
        {
            'b' => 0x00c0,
            'w' => 0x02c0,
            'l' => 0x04c0,
            _ => throw new InvalidOperationException(),
        };
        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(bounds.Value)));
        writer.WriteWord((ushort)((isAddress ? 0x8000 : 0)
            | (register << 12)
            | (trapOnFailure ? 0x0800 : 0)));
        WriteEffectiveAddressExtensions(bounds.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleExtendByte(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "extbの幅は.lです");
            return;
        }

        if (!TryParseOptionalSizedDataRegister(operandText, 'l', out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "extb.lのオペランドはロング幅データレジスタです");
            return;
        }

        writer.WriteWord((ushort)(0x49c0 | register));
    }

    private static void AssemblePackOrUnpack(
        bool pack,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var name = pack ? "pack" : "unpk";
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}に幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 3)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は2つのレジスタと調整即値を必要とします");
            return;
        }

        int source;
        int destination;
        var memoryForm = false;
        if (TryParseOptionalSizedDataRegister(operands[0], 'w', out source)
            && TryParseOptionalSizedDataRegister(operands[1], 'w', out destination))
        {
            // Register form.
        }
        else if (TryParsePredecrementRegister(operands[0], out source)
            && TryParsePredecrementRegister(operands[1], out destination))
        {
            memoryForm = true;
        }
        else
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}はデータレジスタ同士、またはプリデクリメントアドレス同士で指定します");
            return;
        }

        var immediate = ParseImmediateOperand(operands[2], 'w', procedureName, layout, statement, context, diagnostics);
        if (immediate is null)
        {
            return;
        }

        var opcodeBase = pack ? 0x8140 : 0x8180;
        writer.WriteWord((ushort)(opcodeBase
            | (destination << 9)
            | (memoryForm ? 0x0008 : 0)
            | source));
        WriteEffectiveAddressExtensions(immediate.Value, 'w', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleReturnFromModule(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "rtmに幅接尾辞は指定しません");
            return;
        }

        if (!TryParseGeneralRegister(operandText, 'l', out var isAddress, out var register))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "rtmのオペランドはデータ／アドレスレジスタです");
            return;
        }

        writer.WriteWord((ushort)(0x06c0 | (isAddress ? 0x0008 : 0) | register));
    }

    private static void AssembleTrapCondition(
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
        if (operandText.Length == 0)
        {
            if (suffix.Length != 0)
            {
                AddDiagnosticOnce(context, diagnostics, statement, "オペランドなしTRAPccに幅接尾辞は指定しません");
                return;
            }

            writer.WriteWord((ushort)(0x50fc | (condition << 8)));
            return;
        }

        if (suffix is not ("w" or "l"))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "即値付きTRAPccの幅は.wまたは.lです");
            return;
        }

        var size = suffix[0];
        var immediate = ParseImmediateOperand(operandText, size, procedureName, layout, statement, context, diagnostics);
        if (immediate is null)
        {
            return;
        }

        writer.WriteWord((ushort)((size == 'w' ? 0x50fa : 0x50fb) | (condition << 8)));
        WriteEffectiveAddressExtensions(immediate.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleFlushAll(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 || operandText.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "pflushaに幅接尾辞やオペランドは指定しません");
            return;
        }

        writer.WriteWord(0xf000);
        writer.WriteWord(0x2400);
    }

    private static void AssembleFlushMmu(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "pflushに幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count is not (2 or 3)
            || !TryEncodeFunctionCode(operands.ElementAtOrDefault(0) ?? string.Empty, procedureName, statement, context, diagnostics, out var functionCode)
            || !TryParseUnsignedImmediate(operands.ElementAtOrDefault(1) ?? string.Empty, 7, "pflushのマスク", procedureName, statement, context, diagnostics, out var mask))
        {
            if (operands.Count is not (2 or 3))
            {
                AddDiagnosticOnce(context, diagnostics, statement, "pflushはFC,MASKまたはFC,MASK,<ea>で指定します");
            }
            return;
        }

        EffectiveAddress? address = null;
        if (operands.Count == 3)
        {
            address = ParseEffectiveAddress(operands[2], 'l', procedureName, layout, statement, context, diagnostics);
            if (address is null)
            {
                return;
            }

            if (!IsAlterableControlAddress(address.Value))
            {
                AddDiagnosticOnce(context, diagnostics, statement, "pflushの実効アドレスは制御可変アドレスです");
                return;
            }
        }

        writer.WriteWord((ushort)(0xf000 | (address is null ? 0 : EncodeEffectiveAddress(address.Value))));
        writer.WriteWord((ushort)((address is null ? 0x3000 : 0x3800) | (mask << 5) | functionCode));
        if (address is not null)
        {
            WriteEffectiveAddressExtensions(address.Value, 'l', procedureName, statement, writer, context, diagnostics);
        }
    }

    private static void AssembleLoadMmu(
        bool read,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var name = read ? "ploadr" : "ploadw";
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}に幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2
            || !TryEncodeFunctionCode(operands.ElementAtOrDefault(0) ?? string.Empty, procedureName, statement, context, diagnostics, out var functionCode))
        {
            if (operands.Count != 2)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{name}はFCと制御可変アドレスを指定します");
            }
            return;
        }

        var address = ParseEffectiveAddress(operands[1], 'l', procedureName, layout, statement, context, diagnostics);
        if (address is null)
        {
            return;
        }

        if (!IsAlterableControlAddress(address.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の実効アドレスは制御可変アドレスです");
            return;
        }

        writer.WriteWord((ushort)(0xf000 | EncodeEffectiveAddress(address.Value)));
        writer.WriteWord((ushort)(0x2000 | (read ? 0x0200 : 0) | functionCode));
        WriteEffectiveAddressExtensions(address.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleMoveMmu(
        bool noFlush,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var name = noFlush ? "pmovefd" : "pmove";
        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}はMMUレジスタと制御可変アドレスを指定します");
            return;
        }

        var sourceIsRegister = MmuRegisters.TryGetValue(operands[0], out var sourceRegister);
        var destinationIsRegister = MmuRegisters.TryGetValue(operands[1], out var destinationRegister);
        if (sourceIsRegister == destinationIsRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}はMMUレジスタとメモリの間で転送します");
            return;
        }

        var register = sourceIsRegister ? sourceRegister! : destinationRegister!;
        if (suffix.Length != 0
            && (suffix.Length != 1 || suffix[0] != register.Size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}で指定したMMUレジスタの幅は.{register.Size}です");
            return;
        }

        if (noFlush && sourceIsRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "pmovefdはメモリからMMUレジスタへの転送だけに指定できます");
            return;
        }

        if (noFlush && register.Format == MmuRegisterFormat.Status)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "pmovefdはmmusrには指定できません");
            return;
        }

        var memoryToken = sourceIsRegister ? operands[1] : operands[0];
        var address = ParseEffectiveAddress(memoryToken, 'l', procedureName, layout, statement, context, diagnostics);
        if (address is null)
        {
            return;
        }

        if (!IsAlterableControlAddress(address.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}のメモリオペランドは制御可変アドレスです");
            return;
        }

        var extension = register.Format switch
        {
            MmuRegisterFormat.RootOrControl => 0x4000 | (register.Field << 10),
            MmuRegisterFormat.TransparentTranslation => register.Field << 10,
            MmuRegisterFormat.Status => 0x6000,
            _ => throw new InvalidOperationException(),
        };
        if (sourceIsRegister)
        {
            extension |= 0x0200;
        }
        if (noFlush)
        {
            extension |= 0x0100;
        }

        writer.WriteWord((ushort)(0xf000 | EncodeEffectiveAddress(address.Value)));
        writer.WriteWord((ushort)extension);
        WriteEffectiveAddressExtensions(address.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleTestMmu(
        bool read,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var name = read ? "ptestr" : "ptestw";
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}に幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count is not (3 or 4)
            || !TryEncodeFunctionCode(operands.ElementAtOrDefault(0) ?? string.Empty, procedureName, statement, context, diagnostics, out var functionCode)
            || !TryParseUnsignedImmediate(operands.ElementAtOrDefault(2) ?? string.Empty, 7, $"{name}のレベル", procedureName, statement, context, diagnostics, out var level))
        {
            if (operands.Count is not (3 or 4))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{name}はFC,<ea>,#level[,An]で指定します");
            }
            return;
        }

        var address = ParseEffectiveAddress(operands[1], 'l', procedureName, layout, statement, context, diagnostics);
        if (address is null)
        {
            return;
        }

        if (!IsAlterableControlAddress(address.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の実効アドレスは制御可変アドレスです");
            return;
        }

        var returnRegister = 0;
        var returnAddress = operands.Count == 4;
        if (returnAddress
            && (!TryParseAddressRegisterToken(operands[3], out returnRegister) || level == 0))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の返却先はレベル1から7で指定するアドレスレジスタです");
            return;
        }

        writer.WriteWord((ushort)(0xf000 | EncodeEffectiveAddress(address.Value)));
        writer.WriteWord((ushort)(0x8000
            | (level << 10)
            | (read ? 0x0200 : 0)
            | (returnAddress ? 0x0100 | (returnRegister << 5) : 0)
            | functionCode));
        WriteEffectiveAddressExtensions(address.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static bool TryEncodeFunctionCode(
        string token,
        string procedureName,
        RawStatement statement,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics,
        out int encoded)
    {
        encoded = 0;
        var valueToken = token.Trim();
        if (valueToken.Equals("sfc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (valueToken.Equals("dfc", StringComparison.OrdinalIgnoreCase))
        {
            encoded = 1;
            return true;
        }
        if (TryParseOptionalSizedDataRegister(valueToken, 'l', out var register))
        {
            encoded = 0x08 | register;
            return true;
        }
        if (TryParseUnsignedImmediate(valueToken, 7, "ファンクションコード", procedureName, statement, context, diagnostics, out var immediate))
        {
            encoded = 0x10 | immediate;
            return true;
        }

        AddDiagnosticOnce(context, diagnostics, statement, $"ファンクションコードは#0～#7、d0～d7、sfc、dfcです: {token}");
        return false;
    }

    private static bool TryParseUnsignedImmediate(
        string token,
        int maximum,
        string name,
        string procedureName,
        RawStatement statement,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics,
        out int value)
    {
        value = 0;
        var valueToken = token.Trim();
        if (!valueToken.StartsWith('#'))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は即値で指定します");
            return false;
        }

        if (!TryResolveExpression(valueToken[1..], procedureName, statement, context, out var resolved, out var error))
        {
            AddDiagnosticOnce(context, diagnostics, statement, error);
            return false;
        }

        if (resolved < 0 || resolved > maximum)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は0から{maximum}です: {resolved}");
            return false;
        }

        value = checked((int)resolved);
        return true;
    }

    private static bool TryParseDataRegisterPair(string token, char size, out int high, out int low)
    {
        high = 0;
        low = 0;
        var match = DataRegisterPair.Match(token.Trim());
        if (!match.Success
            || !TryParseOptionalSizedDataRegister(match.Groups["high"].Value, size, out high)
            || !TryParseOptionalSizedDataRegister(match.Groups["low"].Value, size, out low))
        {
            return false;
        }

        return high != low;
    }

    private static bool TryParseCas2AddressPair(
        string token,
        out bool firstIsAddress,
        out int first,
        out bool secondIsAddress,
        out int second)
    {
        firstIsAddress = false;
        first = 0;
        secondIsAddress = false;
        second = 0;
        var match = Cas2AddressPair.Match(token.Trim());
        return match.Success
            && TryParseGeneralRegister(match.Groups["first"].Value, 'l', out firstIsAddress, out first)
            && TryParseGeneralRegister(match.Groups["second"].Value, 'l', out secondIsAddress, out second);
    }

    private static bool IsAlterableControlAddress(EffectiveAddress address) =>
        address.Kind is
            EffectiveAddressKind.Indirect
            or EffectiveAddressKind.Displacement
            or EffectiveAddressKind.Indexed
            or EffectiveAddressKind.AbsoluteWord
            or EffectiveAddressKind.AbsoluteLong;

    private static bool RequireOnlyMc68020(
        string instruction,
        AssemblyContext context,
        RawStatement statement,
        List<SourceDiagnostic> diagnostics)
    {
        if (context.Cpu == CpuModel.Mc68020)
        {
            return true;
        }

        AddDiagnosticOnce(
            context,
            diagnostics,
            statement,
            $"{instruction}はMC68020だけの命令です（現在: --cpu {context.Cpu.ToCommandLineName()}）");
        return false;
    }

    private static bool RequireOnlyMc68030(
        string instruction,
        AssemblyContext context,
        RawStatement statement,
        List<SourceDiagnostic> diagnostics)
    {
        if (context.Cpu == CpuModel.Mc68030)
        {
            return true;
        }

        AddDiagnosticOnce(
            context,
            diagnostics,
            statement,
            $"{instruction}はMC68030内蔵MMUの命令です（現在: --cpu {context.Cpu.ToCommandLineName()}）");
        return false;
    }

    private enum MmuRegisterFormat
    {
        RootOrControl,
        TransparentTranslation,
        Status,
    }

    private sealed record MmuRegisterInfo(int Field, MmuRegisterFormat Format, char Size);
}
