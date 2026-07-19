using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace M68kAsm;

public sealed record AssemblyResult(
    byte[] Bytes,
    IReadOnlyList<SourceDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, uint> Symbols)
{
    public bool Success => Diagnostics.Count == 0;
}

public sealed partial class BinaryAssembler
{
    private const string NumberPattern = @"[+-]?(?:\$[0-9A-Fa-f]+|0[xX][0-9A-Fa-f]+|%[01]+|[0-9]+)";
    private const string SymbolPattern = @"(?:\.[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_]*)";

    private static readonly Regex AlgebraDataRegister = new(
        "^[dD](?<register>[0-7])\\.(?<size>[bBwWlL])$",
        RegexOptions.Compiled);

    private static readonly Regex RawDataRegister = new(
        "^[dD](?<register>[0-7])(?:\\.(?<size>[bBwWlL]))?$",
        RegexOptions.Compiled);

    private static readonly Regex AddressRegister = new(
        "^(?:(?:[aA](?<register>[0-7]))|(?<sp>[sS][pP]))(?:\\.[lL])?$",
        RegexOptions.Compiled);

    private static readonly Regex Identifier = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled);

    private static readonly Regex Label = new(
        "^(?<name>(?:\\.[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_]*)):$",
        RegexOptions.Compiled);

    private static readonly Regex BinaryExpression = new(
        "^(?<left>(?:#[+-]?(?:\\$[0-9A-Fa-f]+|0[xX][0-9A-Fa-f]+|%[01]+|[0-9]+)|[A-Za-z_][A-Za-z0-9_]*(?:\\.[bBwWlL])?))\\s*(?<operator>[+-])\\s*(?<right>(?:#[+-]?(?:\\$[0-9A-Fa-f]+|0[xX][0-9A-Fa-f]+|%[01]+|[0-9]+)|[A-Za-z_][A-Za-z0-9_]*(?:\\.[bBwWlL])?))$",
        RegexOptions.Compiled);

    private static readonly Regex ValueExpression = new(
        $"^(?<base>{NumberPattern}|{SymbolPattern})(?:(?<operator>[+-])(?<offset>(?:\\$[0-9A-Fa-f]+|0[xX][0-9A-Fa-f]+|%[01]+|[0-9]+)))?$",
        RegexOptions.Compiled);

    private static readonly Regex PostIncrement = new(
        "^\\((?<register>[aA][0-7]|[sS][pP])\\)\\+$",
        RegexOptions.Compiled);

    private static readonly Regex PreDecrement = new(
        "^-\\((?<register>[aA][0-7]|[sS][pP])\\)$",
        RegexOptions.Compiled);

    private static readonly Regex Indirect = new(
        "^\\((?<register>[aA][0-7]|[sS][pP])\\)$",
        RegexOptions.Compiled);

    private static readonly Regex Indexed = new(
        "^(?<displacement>[^()]*)\\((?<base>[aA][0-7]|[sS][pP]|[pP][cC])\\s*,\\s*(?<index>[dDaA][0-7])\\.(?<indexSize>[wWlL])(?:\\s*\\*\\s*(?<scale>[1248]))?\\)$",
        RegexOptions.Compiled);

    private static readonly Regex BaseSuppressedIndexed = new(
        "^(?<displacement>[^()]*)\\(\\s*,\\s*(?<index>[dDaA][0-7])\\.(?<indexSize>[wWlL])(?:\\s*\\*\\s*(?<scale>[1248]))?\\)$",
        RegexOptions.Compiled);

    private static readonly Regex Displacement = new(
        "^(?<displacement>[^()]*)\\((?<base>[aA][0-7]|[sS][pP]|[pP][cC])\\)$",
        RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, int> BranchConditions =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bra"] = 0x0,
            ["bsr"] = 0x1,
            ["bhi"] = 0x2,
            ["bls"] = 0x3,
            ["bcc"] = 0x4,
            ["bhs"] = 0x4,
            ["bcs"] = 0x5,
            ["blo"] = 0x5,
            ["bne"] = 0x6,
            ["beq"] = 0x7,
            ["bvc"] = 0x8,
            ["bvs"] = 0x9,
            ["bpl"] = 0xa,
            ["bmi"] = 0xb,
            ["bge"] = 0xc,
            ["blt"] = 0xd,
            ["bgt"] = 0xe,
            ["ble"] = 0xf,
        };

    public AssemblyResult Assemble(
        ModuleSyntax module,
        uint baseAddress = 0,
        CpuModel cpu = CpuModel.Mc68000)
    {
        var diagnostics = new List<SourceDiagnostic>();
        var symbols = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var layouts = new Dictionary<ProcedureSyntax, LocalLayout>();
        var structures = CreateStructureLayouts(module.Structures, diagnostics);
        var declarations = new SymbolDeclarations(module);

        foreach (var procedure in module.Procedures)
        {
            layouts.Add(procedure, CreateLocalLayout(procedure, diagnostics));
        }

        if (diagnostics.Count != 0)
        {
            return new AssemblyResult([], diagnostics, symbols);
        }

        var layoutWriter = new BigEndianWriter();
        var layoutContext = new AssemblyContext(
            AssemblyPass.Layout,
            baseAddress,
            cpu,
            symbols,
            declarations,
            module.Constants);
        foreach (var procedure in module.Procedures)
        {
            layoutWriter.Align(2);
            AddSymbol(procedure.Name, procedure.Name, 0, layoutWriter.Position, layoutContext, procedure.Line, 1, diagnostics);
            AssembleProcedure(procedure, layouts[procedure], structures, layoutWriter, layoutContext, diagnostics);
        }

        if (diagnostics.Count != 0)
        {
            return new AssemblyResult([], diagnostics, symbols);
        }

        var writer = new BigEndianWriter();
        var emitContext = new AssemblyContext(
            AssemblyPass.Emit,
            baseAddress,
            cpu,
            symbols,
            declarations,
            module.Constants);
        foreach (var procedure in module.Procedures)
        {
            writer.Align(2);
            AssembleProcedure(procedure, layouts[procedure], structures, writer, emitContext, diagnostics);
        }

        return new AssemblyResult(writer.ToArray(), diagnostics, symbols);
    }

    private static void AssembleProcedure(
        ProcedureSyntax procedure,
        LocalLayout layout,
        IReadOnlyDictionary<string, StructureLayout> structures,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (layout.FrameSize > 0)
        {
            writer.WriteWord(0x4e56); // LINK A6,#displacement
            writer.WriteWord(unchecked((ushort)(short)-layout.FrameSize));
        }

        foreach (var statement in procedure.Statements)
        {
            switch (statement)
            {
                case AlgebraStatement algebra:
                    AssembleAlgebra(algebra, layout, structures, context.Constants, writer, diagnostics);
                    break;
                case RawStatement raw:
                    AssembleRaw(raw, procedure, layout, writer, context, diagnostics);
                    break;
            }
        }

        if ((layout.FrameSize > 0 || procedure.EmitsRts) && (writer.Position & 1) != 0)
        {
            if (context.Pass == AssemblyPass.Layout)
            {
                diagnostics.Add(new SourceDiagnostic(
                    procedure.Line,
                    1,
                    $"手続き '{procedure.Name}' の自動終端が奇数アドレスになります。終端前にevenを指定してください"));
            }

            writer.Align(2);
        }

        if (layout.FrameSize > 0)
        {
            writer.WriteWord(0x4e5e); // UNLK A6
        }

        if (procedure.EmitsRts)
        {
            writer.WriteWord(0x4e75); // RTS
        }
    }

    private static LocalLayout CreateLocalLayout(
        ProcedureSyntax procedure,
        List<SourceDiagnostic> diagnostics)
    {
        var slots = new Dictionary<string, LocalSlot>(StringComparer.OrdinalIgnoreCase);
        var usedBytes = 0;

        foreach (var local in procedure.Locals)
        {
            var size = TypeToSize(local.Type);
            var byteCount = SizeInBytes(size);
            var alignment = byteCount == 1 ? 1 : 2;
            usedBytes = Align(usedBytes, alignment);
            usedBytes += byteCount;

            if (usedBytes > short.MaxValue)
            {
                diagnostics.Add(new SourceDiagnostic(
                    local.Line,
                    local.Column,
                    $"手続き '{procedure.Name}' のローカル領域が32,767バイトを超えています"));
                return new LocalLayout(slots, usedBytes);
            }

            slots.Add(local.Name, new LocalSlot(size, checked((short)-usedBytes)));
        }

        return new LocalLayout(slots, Align(usedBytes, 2));
    }

    private static IReadOnlyDictionary<string, StructureLayout> CreateStructureLayouts(
        IEnumerable<StructureSyntax> structures,
        List<SourceDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, StructureLayout>(StringComparer.OrdinalIgnoreCase);
        foreach (var structure in structures)
        {
            var fields = new Dictionary<string, StructureFieldLayout>(StringComparer.OrdinalIgnoreCase);
            var offset = 0;
            foreach (var field in structure.Fields)
            {
                var size = TypeToSize(field.Type);
                var byteCount = SizeInBytes(size);
                offset = Align(offset, byteCount == 1 ? 1 : 2);
                if (offset > short.MaxValue)
                {
                    diagnostics.Add(new SourceDiagnostic(
                        field.Line,
                        field.Column,
                        $"構造体 '{structure.Name}' のフィールド '{field.Name}' は16ビット変位の範囲外です"));
                    continue;
                }

                fields.Add(field.Name, new StructureFieldLayout(size, checked((short)offset)));
                offset = checked(offset + byteCount);
            }

            result.Add(structure.Name, new StructureLayout(structure.BaseRegister, fields, Align(offset, 2)));
        }

        return result;
    }

    private static void AssembleAlgebra(
        AlgebraStatement statement,
        LocalLayout layout,
        IReadOnlyDictionary<string, StructureLayout> structures,
        IReadOnlyDictionary<string, long> constants,
        BigEndianWriter writer,
        List<SourceDiagnostic> diagnostics)
    {
        var destination = ParseAlgebraOperand(
            statement.Destination,
            expectedSize: null,
            allowImmediate: false,
            layout,
            structures,
            constants,
            statement,
            diagnostics);
        if (destination is null)
        {
            return;
        }

        if (statement.Operator == "=")
        {
            AssembleAssignment(statement, destination.Value, layout, structures, constants, writer, diagnostics);
            return;
        }

        var source = ParseAlgebraOperand(
            statement.Expression,
            destination.Value.Size,
            allowImmediate: true,
            layout,
            structures,
            constants,
            statement,
            diagnostics);
        if (source is null)
        {
            return;
        }

        EmitAlgebraAddOrSubtract(
            statement.Operator == "+=",
            source.Value,
            destination.Value,
            statement,
            writer,
            diagnostics);
    }

    private static void AssembleAssignment(
        AlgebraStatement statement,
        AlgebraOperand destination,
        LocalLayout layout,
        IReadOnlyDictionary<string, StructureLayout> structures,
        IReadOnlyDictionary<string, long> constants,
        BigEndianWriter writer,
        List<SourceDiagnostic> diagnostics)
    {
        var expression = statement.Expression.Trim();
        var binary = BinaryExpression.Match(expression);
        if (!binary.Success)
        {
            var source = ParseAlgebraOperand(
                expression,
                destination.Size,
                allowImmediate: true,
                layout,
                structures,
                constants,
                statement,
                diagnostics);
            if (source is not null)
            {
                EmitAlgebraMove(source.Value, destination, statement, writer, diagnostics);
            }

            return;
        }

        var left = ParseAlgebraOperand(
            binary.Groups["left"].Value,
            destination.Size,
            allowImmediate: true,
            layout,
            structures,
            constants,
            statement,
            diagnostics);
        var right = ParseAlgebraOperand(
            binary.Groups["right"].Value,
            destination.Size,
            allowImmediate: true,
            layout,
            structures,
            constants,
            statement,
            diagnostics);
        if (left is null || right is null)
        {
            return;
        }

        if (!SameStorage(left.Value, destination))
        {
            EmitAlgebraMove(left.Value, destination, statement, writer, diagnostics);
        }

        EmitAlgebraAddOrSubtract(
            binary.Groups["operator"].Value == "+",
            right.Value,
            destination,
            statement,
            writer,
            diagnostics);
    }

    private static AlgebraOperand? ParseAlgebraOperand(
        string text,
        char? expectedSize,
        bool allowImmediate,
        LocalLayout layout,
        IReadOnlyDictionary<string, StructureLayout> structures,
        IReadOnlyDictionary<string, long> constants,
        StatementSyntax statement,
        List<SourceDiagnostic> diagnostics)
    {
        var token = text.Trim();
        var register = AlgebraDataRegister.Match(token);
        if (register.Success)
        {
            var size = char.ToLowerInvariant(register.Groups["size"].Value[0]);
            if (expectedSize is not null && size != expectedSize)
            {
                diagnostics.Add(new SourceDiagnostic(
                    statement.Line,
                    statement.Column,
                    $"オペランド '{token}' の幅が出力先と一致しません"));
                return null;
            }

            return AlgebraOperand.DataRegister(register.Groups["register"].Value[0] - '0', size);
        }

        if (token.StartsWith('#'))
        {
            if (!allowImmediate)
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, "即値を出力先にはできません"));
                return null;
            }

            if (expectedSize is null)
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"即値 '{token}' の幅を決定できません"));
                return null;
            }

            if (!ExpressionEvaluator.TryEvaluate(
                    token[1..],
                    name => constants.TryGetValue(name, out var constant)
                        ? SymbolResolution.Resolved(constant)
                        : SymbolResolution.Failed($"代数記法の即値式では定数ラベルだけを利用できます: {name}"),
                    out var value,
                    out var error))
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, error));
                return null;
            }

            if (!FitsSize(value, expectedSize.Value))
            {
                diagnostics.Add(new SourceDiagnostic(
                    statement.Line,
                    statement.Column,
                    $"即値 '{token}' は.{expectedSize.Value}の範囲外です"));
                return null;
            }

            return AlgebraOperand.Immediate(value, expectedSize.Value);
        }

        if (Identifier.IsMatch(token) && layout.Slots.TryGetValue(token, out var slot))
        {
            if (expectedSize is not null && slot.Size != expectedSize)
            {
                diagnostics.Add(new SourceDiagnostic(
                    statement.Line,
                    statement.Column,
                    $"ローカル変数 '{token}' の幅が出力先と一致しません"));
                return null;
            }

            return AlgebraOperand.Local(slot.Offset, slot.Size);
        }

        var structureParts = token.Split(',', 2, StringSplitOptions.TrimEntries);
        if (structureParts.Length == 2
            && structures.TryGetValue(structureParts[0], out var structure)
            && structure.Fields.TryGetValue(structureParts[1], out var field))
        {
            if (structure.BaseRegister is null)
            {
                diagnostics.Add(new SourceDiagnostic(
                    statement.Line,
                    statement.Column,
                    $"構造体 '{structureParts[0]}' にはbaseレジスタが指定されていません"));
                return null;
            }

            if (expectedSize is not null && field.Size != expectedSize)
            {
                diagnostics.Add(new SourceDiagnostic(
                    statement.Line,
                    statement.Column,
                    $"構造体フィールド '{token}' の幅が出力先と一致しません"));
                return null;
            }

            return AlgebraOperand.StructureField(
                structure.BaseRegister.Value,
                field.Offset,
                field.Size);
        }

        diagnostics.Add(new SourceDiagnostic(
            statement.Line,
            statement.Column,
            $"機械語出力で未対応の代数オペランドです: {token}"));
        return null;
    }

    private static void EmitAlgebraMove(
        AlgebraOperand source,
        AlgebraOperand destination,
        StatementSyntax statement,
        BigEndianWriter writer,
        List<SourceDiagnostic> diagnostics)
    {
        if (source.Size != destination.Size)
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, "MOVE元とMOVE先の幅が一致しません"));
            return;
        }

        if (IsMemoryOperand(source) && IsMemoryOperand(destination))
        {
            diagnostics.Add(new SourceDiagnostic(
                statement.Line,
                statement.Column,
                "ローカル変数間の直接代入はできません。データレジスタを明示してください"));
            return;
        }

        if (source.Kind == AlgebraOperandKind.Immediate
            && source.Size == 'l'
            && destination.Kind == AlgebraOperandKind.DataRegister
            && source.Value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            writer.WriteWord((ushort)(
                0x7000
                | (destination.Register << 9)
                | (byte)(sbyte)source.Value));
            return;
        }

        var opcode = MoveBase(source.Size)
            | EncodeAlgebraMoveDestination(destination)
            | EncodeAlgebraEffectiveAddress(source);
        writer.WriteWord(checked((ushort)opcode));
        WriteAlgebraExtension(source, writer);
        WriteAlgebraExtension(destination, writer);
    }

    private static void EmitAlgebraAddOrSubtract(
        bool isAdd,
        AlgebraOperand source,
        AlgebraOperand destination,
        StatementSyntax statement,
        BigEndianWriter writer,
        List<SourceDiagnostic> diagnostics)
    {
        if (source.Size != destination.Size)
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, "演算元と演算先の幅が一致しません"));
            return;
        }

        if (source.Kind == AlgebraOperandKind.Immediate)
        {
            var quickValue = source.Value;
            var quickIsAdd = isAdd;
            if (quickValue < 0)
            {
                quickValue = -quickValue;
                quickIsAdd = !quickIsAdd;
            }

            if (quickValue is >= 1 and <= 8)
            {
                var quickField = quickValue == 8 ? 0 : checked((int)quickValue);
                var quickOpcode = (quickIsAdd ? 0x5000 : 0x5100)
                    | (quickField << 9)
                    | SizeBits(destination.Size)
                    | EncodeAlgebraEffectiveAddress(destination);
                writer.WriteWord(checked((ushort)quickOpcode));
                WriteAlgebraExtension(destination, writer);
                return;
            }

            var opcode = (isAdd ? 0x0600 : 0x0400)
                | SizeBits(destination.Size)
                | EncodeAlgebraEffectiveAddress(destination);
            writer.WriteWord(checked((ushort)opcode));
            WriteImmediate(source.Value, source.Size, writer);
            WriteAlgebraExtension(destination, writer);
            return;
        }

        if (destination.Kind == AlgebraOperandKind.DataRegister)
        {
            var opcode = (isAdd ? 0xd000 : 0x9000)
                | (destination.Register << 9)
                | SizeBits(destination.Size)
                | EncodeAlgebraEffectiveAddress(source);
            writer.WriteWord(checked((ushort)opcode));
            WriteAlgebraExtension(source, writer);
            return;
        }

        if (IsMemoryOperand(destination) && source.Kind == AlgebraOperandKind.DataRegister)
        {
            var opcode = (isAdd ? 0xd100 : 0x9100)
                | (source.Register << 9)
                | SizeBits(destination.Size)
                | EncodeAlgebraEffectiveAddress(destination);
            writer.WriteWord(checked((ushort)opcode));
            WriteAlgebraExtension(destination, writer);
            return;
        }

        diagnostics.Add(new SourceDiagnostic(
            statement.Line,
            statement.Column,
            "このメモリ間演算はMC68000命令に直接変換できません。データレジスタを明示してください"));
    }

    private static void AssembleRaw(
        RawStatement statement,
        ProcedureSyntax procedure,
        LocalLayout layout,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var text = statement.Text.Trim();
        var label = Label.Match(text);
        if (label.Success)
        {
            if (context.Pass == AssemblyPass.Layout)
            {
                AddSymbol(
                    label.Groups["name"].Value,
                    procedure.Name,
                    statement.ScopeId,
                    writer.Position,
                    context,
                    statement.Line,
                    statement.Column,
                    diagnostics);
            }

            return;
        }

        var split = text.IndexOfAny([' ', '\t']);
        var mnemonicToken = split < 0 ? text : text[..split];
        var operandText = split < 0 ? string.Empty : text[(split + 1)..].Trim();
        var suffixIndex = mnemonicToken.LastIndexOf('.');
        var mnemonic = (suffixIndex < 0 ? mnemonicToken : mnemonicToken[..suffixIndex]).ToLowerInvariant();
        var suffix = suffixIndex < 0 ? string.Empty : mnemonicToken[(suffixIndex + 1)..].ToLowerInvariant();

        if (mnemonic == "even")
        {
            if (operandText.Length != 0)
            {
                AddDiagnosticOnce(context, diagnostics, statement, "evenにオペランドは指定できません");
                return;
            }

            writer.Align(2);
            return;
        }

        if (mnemonic == "align")
        {
            AssembleAlignment(suffix, operandText, statement, writer, context, diagnostics);
            return;
        }

        if (mnemonic == "dc")
        {
            AssembleData(suffix, operandText, procedure.Name, statement, writer, context, diagnostics);
            return;
        }

        if (mnemonic is "defb" or "defw" or "defl")
        {
            if (suffix.Length != 0)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}に幅接尾辞は指定しません");
                return;
            }

            var dataSize = mnemonic[^1] switch
            {
                'b' => "b",
                'w' => "w",
                'l' => "l",
                _ => throw new InvalidOperationException(),
            };
            AssembleData(dataSize, operandText, procedure.Name, statement, writer, context, diagnostics);
            return;
        }

        if (mnemonic == "defs")
        {
            AssembleStrings(suffix, operandText, statement, writer, context, diagnostics);
            return;
        }

        if ((writer.Position & 1) != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "命令を奇数アドレスへ配置できません。直前にevenを指定してください");
            return;
        }

        if (BranchConditions.TryGetValue(mnemonic, out var condition))
        {
            AssembleBranch(condition, suffix, operandText, procedure.Name, statement, writer, context, diagnostics);
            return;
        }

        if (TryAssembleMc68000Instruction(
                mnemonic,
                suffix,
                operandText,
                procedure,
                layout,
                statement,
                writer,
                context,
                diagnostics))
        {
            return;
        }

        AddDiagnosticOnce(context, diagnostics, statement, $"機械語出力で未対応の文です: {statement.Text}");
    }

    private static void AssembleMoveQuick(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "moveqの幅指定は省略するか.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "moveqは2つのオペランドを必要とします");
            return;
        }

        var immediate = operands[0].Trim();
        if (!immediate.StartsWith('#') || !IsValueExpression(immediate[1..]))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "moveqの第1オペランドは即値です");
            return;
        }

        var destination = ParseEffectiveAddress(
            operands[1],
            'l',
            procedureName,
            layout,
            statement,
            context,
            diagnostics);
        if (destination is null)
        {
            return;
        }

        if (destination.Value.Kind != EffectiveAddressKind.DataRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "moveqの出力先はロング幅データレジスタです");
            return;
        }

        if (!TryResolveExpression(
                immediate[1..],
                procedureName,
                statement,
                context,
                out var value,
                out var error))
        {
            if (context.Pass == AssemblyPass.Layout)
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, error));
            }

            writer.WriteWord(0);
            return;
        }

        if (context.Pass == AssemblyPass.Emit && value is < sbyte.MinValue or > sbyte.MaxValue)
        {
            diagnostics.Add(new SourceDiagnostic(
                statement.Line,
                statement.Column,
                $"moveqの即値は-128から127で指定します: {value}"));
            writer.WriteWord(0);
            return;
        }

        writer.WriteWord((ushort)(
            0x7000
            | (destination.Value.Register << 9)
            | (byte)(sbyte)value));
    }

    private static void AssembleQuickArithmetic(
        bool isAdd,
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var name = isAdd ? "addq" : "subq";
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

        var immediate = operands[0].Trim();
        if (!immediate.StartsWith('#') || !IsValueExpression(immediate[1..]))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の第1オペランドは1から8の即値です");
            return;
        }

        var destination = ParseEffectiveAddress(
            operands[1],
            size,
            procedureName,
            layout,
            statement,
            context,
            diagnostics);
        if (destination is null)
        {
            return;
        }

        var addressRegisterDestination = destination.Value.Kind == EffectiveAddressKind.AddressRegister;
        if ((!IsDataAlterable(destination.Value) && !addressRegisterDestination)
            || (addressRegisterDestination && size == 'b'))
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                $"{name}の出力先にはデータ可変実効アドレス、またはワード／ロング幅アドレスレジスタを指定します");
            return;
        }

        if (!TryEvaluateConstant(
                immediate[1..],
                context.Constants,
                out var value,
                out var error))
        {
            AddDiagnosticOnce(context, diagnostics, statement, error);
            return;
        }

        if (value is < 1 or > 8)
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                $"{name}の即値は1から8で指定します: {value}");
            return;
        }

        var quickField = value == 8 ? 0 : checked((int)value);
        var opcode = (isAdd ? 0x5000 : 0x5100)
            | (quickField << 9)
            | SizeBits(size)
            | EncodeEffectiveAddress(destination.Value);
        writer.WriteWord(checked((ushort)opcode));
        WriteEffectiveAddressExtensions(
            destination.Value,
            size,
            procedureName,
            statement,
            writer,
            context,
            diagnostics);
    }

    private static void AssembleRawDataToRegister(
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
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(
            operands[0],
            size,
            procedureName,
            layout,
            statement,
            context,
            diagnostics);
        var destination = ParseEffectiveAddress(
            operands[1],
            size,
            procedureName,
            layout,
            statement,
            context,
            diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (!IsDataAddress(source.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}のソースにはデータ実効アドレスを指定します");
            return;
        }

        if (destination.Value.Kind != EffectiveAddressKind.DataRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の出力先はデータレジスタです");
            return;
        }

        var opcode = opcodeBase
            | (destination.Value.Register << 9)
            | SizeBits(size)
            | EncodeEffectiveAddress(source.Value);
        writer.WriteWord(checked((ushort)opcode));
        WriteEffectiveAddressExtensions(
            source.Value,
            size,
            procedureName,
            statement,
            writer,
            context,
            diagnostics);
    }

    private static void AssembleWordMultiplyOrDivide(
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
        if (suffix != "w")
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                $"{mnemonic}はMC68000のワード形式だけを扱うため.wを指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}は2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(
            operands[0],
            'w',
            procedureName,
            layout,
            statement,
            context,
            diagnostics);
        var destination = ParseEffectiveAddress(
            operands[1],
            'l',
            procedureName,
            layout,
            statement,
            context,
            diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (!IsDataAddress(source.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}のソースにはワード幅データ実効アドレスを指定します");
            return;
        }

        if (destination.Value.Kind != EffectiveAddressKind.DataRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{mnemonic}の出力先はロング幅データレジスタです");
            return;
        }

        var opcodeBase = mnemonic switch
        {
            "mulu" => 0xc0c0,
            "muls" => 0xc1c0,
            "divu" => 0x80c0,
            "divs" => 0x81c0,
            _ => throw new InvalidOperationException($"未知の乗除算命令です: {mnemonic}"),
        };
        var opcode = opcodeBase
            | (destination.Value.Register << 9)
            | EncodeEffectiveAddress(source.Value);
        writer.WriteWord(checked((ushort)opcode));
        WriteEffectiveAddressExtensions(
            source.Value,
            'w',
            procedureName,
            statement,
            writer,
            context,
            diagnostics);
    }

    private static void AssembleShiftOrRotate(
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
        var shiftLeft = name is "asl" or "lsl" or "roxl" or "rol";
        var operation = name switch
        {
            "asr" or "asl" => 0,
            "lsr" or "lsl" => 1,
            "roxr" or "roxl" => 2,
            "ror" or "rol" => 3,
            _ => throw new InvalidOperationException($"未知のシフト／ローテート命令です: {name}"),
        };
        var operands = SplitOperands(operandText);
        if (operands.Count == 1)
        {
            if (suffix.Length != 0 && suffix != "w")
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{name}のメモリ形式は.wだけを使用できます");
                return;
            }

            var destination = ParseEffectiveAddress(
                operands[0],
                'w',
                procedureName,
                layout,
                statement,
                context,
                diagnostics);
            if (destination is null)
            {
                return;
            }

            if (!IsMemoryAlterable(destination.Value))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"{name}の1オペランド形式にはメモリ可変実効アドレスを指定します");
                return;
            }

            var opcode = 0xe0c0
                | (operation << 9)
                | (shiftLeft ? 0x0100 : 0)
                | EncodeEffectiveAddress(destination.Value);
            writer.WriteWord(checked((ushort)opcode));
            WriteEffectiveAddressExtensions(destination.Value, 'w', procedureName, statement, writer, context, diagnostics);
            return;
        }

        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は1つまたは2つのオペランドを必要とします");
            return;
        }

        if (!TryParseInstructionSize(suffix, out var size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}のレジスタ形式は.b、.w、.lで幅を指定します");
            return;
        }

        var destinationRegister = ParseEffectiveAddress(
            operands[1],
            size,
            procedureName,
            layout,
            statement,
            context,
            diagnostics);
        if (destinationRegister is null)
        {
            return;
        }

        if (destinationRegister.Value.Kind != EffectiveAddressKind.DataRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の2オペランド形式の出力先はデータレジスタです");
            return;
        }

        var countField = 0;
        var registerCount = false;
        var countToken = operands[0].Trim();
        if (countToken.StartsWith('#'))
        {
            if (!TryEvaluateConstant(countToken[1..], context.Constants, out var count, out var countError)
                || count is < 1 or > 8)
            {
                AddDiagnosticOnce(
                    context,
                    diagnostics,
                    statement,
                    countError.Length == 0 ? $"{name}の即値シフト回数は1から8で指定します" : countError);
                return;
            }

            countField = count == 8 ? 0 : checked((int)count);
        }
        else
        {
            var countRegister = RawDataRegister.Match(countToken);
            if (!countRegister.Success || !countRegister.Groups["size"].Success)
            {
                AddDiagnosticOnce(
                    context,
                    diagnostics,
                    statement,
                    $"{name}のシフト回数レジスタはd0.b～d7.lの形式で指定します");
                return;
            }

            countField = countRegister.Groups["register"].Value[0] - '0';
            registerCount = true;
        }

        var registerOpcode = 0xe000
            | (countField << 9)
            | (shiftLeft ? 0x0100 : 0)
            | SizeBits(size)
            | (registerCount ? 0x0020 : 0)
            | (operation << 3)
            | destinationRegister.Value.Register;
        writer.WriteWord(checked((ushort)registerOpcode));
    }

    private static void AssembleImmediateLogical(
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
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], size, procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (source.Value.Kind != EffectiveAddressKind.Immediate)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の第1オペランドは即値です");
            return;
        }

        if (!IsDataAlterable(destination.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の出力先にはデータ可変実効アドレスを指定します");
            return;
        }

        writer.WriteWord(checked((ushort)(opcodeBase | SizeBits(size) | EncodeEffectiveAddress(destination.Value))));
        WriteEffectiveAddressExtensions(source.Value, size, procedureName, statement, writer, context, diagnostics);
        WriteEffectiveAddressExtensions(destination.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleRawOr(
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
            AddDiagnosticOnce(context, diagnostics, statement, "orの幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "orは2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], size, procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (destination.Value.Kind == EffectiveAddressKind.DataRegister)
        {
            if (!IsDataAddress(source.Value))
            {
                AddDiagnosticOnce(context, diagnostics, statement, "orのデータレジスタ出力形式ではソースにデータ実効アドレスを指定します");
                return;
            }

            var opcode = 0x8000
                | (destination.Value.Register << 9)
                | SizeBits(size)
                | EncodeEffectiveAddress(source.Value);
            writer.WriteWord(checked((ushort)opcode));
            WriteEffectiveAddressExtensions(source.Value, size, procedureName, statement, writer, context, diagnostics);
            return;
        }

        if (source.Value.Kind != EffectiveAddressKind.DataRegister || !IsMemoryAlterable(destination.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "orのメモリ出力形式はデータレジスタからメモリ可変実効アドレスへ指定します");
            return;
        }

        var memoryOpcode = 0x8100
            | (source.Value.Register << 9)
            | SizeBits(size)
            | EncodeEffectiveAddress(destination.Value);
        writer.WriteWord(checked((ushort)memoryOpcode));
        WriteEffectiveAddressExtensions(destination.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleBranch(
        int condition,
        string suffix,
        string targetExpression,
        string procedureName,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var shortBranch = suffix is "s" or "b";
        var longBranch = suffix == "l";
        if (suffix.Length != 0 && !shortBranch && !longBranch && suffix != "w")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "分岐幅は.s、.w、またはMC68020以降の.lで指定します");
            return;
        }

        if (longBranch && context.Cpu < CpuModel.Mc68020)
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                $"ロング分岐はMC68020以降です（現在: --cpu {context.Cpu.ToCommandLineName()}）");
            return;
        }

        if (!IsValueExpression(targetExpression))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"分岐先を解釈できません: {targetExpression}");
            return;
        }

        var instructionPosition = writer.Position;
        if (context.Pass == AssemblyPass.Layout)
        {
            if (!TryResolveExpression(targetExpression, procedureName, statement, context, out _, out var layoutError))
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, layoutError));
            }

            writer.WriteWord(0);
            if (longBranch)
            {
                writer.WriteLong(0);
            }
            else if (!shortBranch)
            {
                writer.WriteWord(0);
            }

            return;
        }

        if (!TryResolveExpression(targetExpression, procedureName, statement, context, out var target, out var error))
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, error));
            writer.WriteWord(0);
            if (longBranch)
            {
                writer.WriteLong(0);
            }
            else if (!shortBranch)
            {
                writer.WriteWord(0);
            }

            return;
        }

        var pc = (long)context.BaseAddress + instructionPosition + 2;
        var displacement = target - pc;
        var opcode = 0x6000 | (condition << 8);
        if (longBranch)
        {
            if (displacement is < int.MinValue or > int.MaxValue)
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"ロング分岐の変位が32ビット範囲外です: {displacement}"));
                writer.WriteWord(0);
                writer.WriteLong(0);
                return;
            }

            writer.WriteWord((ushort)(opcode | 0x00ff));
            writer.WriteLong(unchecked((uint)(int)displacement));
            return;
        }

        if (shortBranch)
        {
            if (displacement is < sbyte.MinValue or > sbyte.MaxValue || displacement == 0)
            {
                diagnostics.Add(new SourceDiagnostic(
                    statement.Line,
                    statement.Column,
                    $"短分岐の変位が8ビット範囲外、または0です: {displacement}"));
                writer.WriteWord(0);
                return;
            }

            writer.WriteWord((ushort)(opcode | (byte)(sbyte)displacement));
            return;
        }

        if (displacement is < short.MinValue or > short.MaxValue)
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"ワード分岐の変位が16ビット範囲外です: {displacement}"));
            writer.WriteWord(0);
            writer.WriteWord(0);
            return;
        }

        writer.WriteWord((ushort)opcode);
        writer.WriteWord(unchecked((ushort)(short)displacement));
    }

    private static void AssembleRawMove(
        bool requireAddressDestination,
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
            AddDiagnosticOnce(context, diagnostics, statement, "moveの幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "moveは2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], size, procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], size, procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (destination.Value.Kind is EffectiveAddressKind.Immediate or EffectiveAddressKind.PcDisplacement or EffectiveAddressKind.PcIndexed)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "MOVEの出力先に即値またはPC相対アドレスは使えません");
            return;
        }

        if (requireAddressDestination && destination.Value.Kind != EffectiveAddressKind.AddressRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "moveaの出力先はアドレスレジスタです");
            return;
        }

        if ((source.Value.Kind == EffectiveAddressKind.AddressRegister || destination.Value.Kind == EffectiveAddressKind.AddressRegister)
            && size == 'b')
        {
            AddDiagnosticOnce(context, diagnostics, statement, "アドレスレジスタにbyte幅のMOVEは使えません");
            return;
        }

        var opcode = MoveBase(size)
            | (destination.Value.Register << 9)
            | (destination.Value.Mode << 6)
            | EncodeEffectiveAddress(source.Value);
        writer.WriteWord(checked((ushort)opcode));
        WriteEffectiveAddressExtensions(source.Value, size, procedureName, statement, writer, context, diagnostics);
        WriteEffectiveAddressExtensions(destination.Value, size, procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleLea(
        string suffix,
        string operandText,
        string procedureName,
        LocalLayout layout,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0 && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, "leaの幅指定は省略するか.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "leaは2つのオペランドを必要とします");
            return;
        }

        var source = ParseEffectiveAddress(operands[0], 'l', procedureName, layout, statement, context, diagnostics);
        var destination = ParseEffectiveAddress(operands[1], 'l', procedureName, layout, statement, context, diagnostics);
        if (source is null || destination is null)
        {
            return;
        }

        if (!IsControlAddress(source.Value) || destination.Value.Kind != EffectiveAddressKind.AddressRegister)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "leaは制御アドレスとアドレスレジスタを必要とします");
            return;
        }

        writer.WriteWord((ushort)(0x41c0 | (destination.Value.Register << 9) | EncodeEffectiveAddress(source.Value)));
        WriteEffectiveAddressExtensions(source.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleSingleControlInstruction(
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
        if (suffix.Length != 0 && suffix != "l")
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}の幅指定は省略するか.lです");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count != 1)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}は1つのオペランドを必要とします");
            return;
        }

        var address = ParseEffectiveAddress(operands[0], 'l', procedureName, layout, statement, context, diagnostics);
        if (address is null)
        {
            return;
        }

        if (!IsControlAddress(address.Value))
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"{name}には制御アドレスを指定します");
            return;
        }

        writer.WriteWord((ushort)(opcodeBase | EncodeEffectiveAddress(address.Value)));
        WriteEffectiveAddressExtensions(address.Value, 'l', procedureName, statement, writer, context, diagnostics);
    }

    private static void AssembleAlignment(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "alignに幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count is < 1 or > 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "alignは 'align 境界[, 詰め値]' で指定します");
            return;
        }

        if (!TryEvaluateConstant(operands[0], context.Constants, out var boundary, out var boundaryError)
            || boundary is <= 0 or > int.MaxValue)
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                boundaryError.Length == 0 ? $"align境界が範囲外です: {operands[0]}" : boundaryError);
            return;
        }

        long fill = 0;
        if (operands.Count == 2
            && (!TryEvaluateConstant(operands[1], context.Constants, out fill, out var fillError) || fill is < 0 or > byte.MaxValue))
        {
            AddDiagnosticOnce(
                context,
                diagnostics,
                statement,
                fillError.Length == 0 ? $"alignの詰め値は0から255で指定します: {operands[1]}" : fillError);
            return;
        }

        writer.Align(checked((int)boundary), (byte)fill);
    }

    private static void AssembleStrings(
        string suffix,
        string operandText,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (suffix.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "defsに幅接尾辞は指定しません");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count == 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "defsには1個以上の文字列を指定します");
            return;
        }

        foreach (var operand in operands)
        {
            if (!TryParseByteString(operand, out var bytes, out var error))
            {
                AddDiagnosticOnce(context, diagnostics, statement, error);
                continue;
            }

            writer.WriteBytes(bytes);
        }
    }

    private static void AssembleData(
        string suffix,
        string operandText,
        string procedureName,
        RawStatement statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (!TryParseInstructionSize(suffix, out var size))
        {
            AddDiagnosticOnce(context, diagnostics, statement, "dcの幅は.b、.w、.lで指定します");
            return;
        }

        var operands = SplitOperands(operandText);
        if (operands.Count == 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, "dcには数値またはシンボルをカンマ区切りで指定します");
            return;
        }

        foreach (var operand in operands)
        {
            if (operand.TrimStart().StartsWith('"'))
            {
                if (size != 'b')
                {
                    AddDiagnosticOnce(context, diagnostics, statement, "文字列はdc.b、defb、defsでのみ定義できます");
                    continue;
                }

                if (!TryParseByteString(operand, out var bytes, out var stringError))
                {
                    AddDiagnosticOnce(context, diagnostics, statement, stringError);
                    continue;
                }

                writer.WriteBytes(bytes);
                continue;
            }

            if (!IsValueExpression(operand))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"データ式を解釈できません: {operand}");
                continue;
            }

            if (context.Pass == AssemblyPass.Layout)
            {
                if (!TryResolveExpression(operand, procedureName, statement, context, out _, out var layoutError))
                {
                    diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, layoutError));
                }

                writer.WriteZeroes(SizeInBytes(size));
                continue;
            }

            if (!TryResolveExpression(operand, procedureName, statement, context, out var value, out var error))
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, error));
                writer.WriteZeroes(SizeInBytes(size));
                continue;
            }

            if (!FitsSize(value, size))
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"dc.{size}の値が範囲外です: {value}"));
                writer.WriteZeroes(SizeInBytes(size));
                continue;
            }

            switch (size)
            {
                case 'b':
                    writer.WriteByte(unchecked((byte)value));
                    break;
                case 'w':
                    writer.WriteWord(unchecked((ushort)value));
                    break;
                case 'l':
                    writer.WriteLong(unchecked((uint)value));
                    break;
            }
        }
    }

    private static EffectiveAddress? ParseEffectiveAddress(
        string text,
        char instructionSize,
        string procedureName,
        LocalLayout layout,
        StatementSyntax statement,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var token = text.Trim();
        var dataRegister = RawDataRegister.Match(token);
        if (dataRegister.Success)
        {
            if (!dataRegister.Groups["size"].Success)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"データレジスタ '{token}' には.b、.w、.lの幅指定が必要です");
                return null;
            }

            var explicitSize = char.ToLowerInvariant(dataRegister.Groups["size"].Value[0]);
            if (explicitSize != instructionSize)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"データレジスタ '{token}' の幅が命令幅と一致しません");
                return null;
            }

            return new EffectiveAddress(
                EffectiveAddressKind.DataRegister,
                0,
                dataRegister.Groups["register"].Value[0] - '0',
                null,
                null);
        }

        var addressRegister = AddressRegister.Match(token);
        if (addressRegister.Success)
        {
            return new EffectiveAddress(
                EffectiveAddressKind.AddressRegister,
                1,
                ParseAddressRegister(addressRegister.Value),
                null,
                null);
        }

        if (token.StartsWith('#'))
        {
            var expression = token[1..].Trim();
            if (!IsValueExpression(expression))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"即値を解釈できません: {token}");
                return null;
            }

            return new EffectiveAddress(EffectiveAddressKind.Immediate, 7, 4, expression, null);
        }

        if (Identifier.IsMatch(token) && layout.Slots.TryGetValue(token, out var local))
        {
            return new EffectiveAddress(EffectiveAddressKind.Displacement, 5, 6, local.Offset.ToString(CultureInfo.InvariantCulture), null);
        }

        var postIncrement = PostIncrement.Match(token);
        if (postIncrement.Success)
        {
            return new EffectiveAddress(EffectiveAddressKind.PostIncrement, 3, ParseAddressRegister(postIncrement.Groups["register"].Value), null, null);
        }

        var preDecrement = PreDecrement.Match(token);
        if (preDecrement.Success)
        {
            return new EffectiveAddress(EffectiveAddressKind.PreDecrement, 4, ParseAddressRegister(preDecrement.Groups["register"].Value), null, null);
        }

        var indirect = Indirect.Match(token);
        if (indirect.Success)
        {
            return new EffectiveAddress(EffectiveAddressKind.Indirect, 2, ParseAddressRegister(indirect.Groups["register"].Value), null, null);
        }

        if (TryParseFullMemoryIndirect(
            token,
            statement,
            context,
            diagnostics,
            out var fullMemoryIndirect))
        {
            return fullMemoryIndirect;
        }

        var baseSuppressedIndexed = BaseSuppressedIndexed.Match(token);
        if (baseSuppressedIndexed.Success)
        {
            if (!RequireCpu(CpuModel.Mc68020, "ベース抑止インデックス", context, statement, diagnostics))
            {
                return null;
            }

            var displacementText = baseSuppressedIndexed.Groups["displacement"].Value.Trim();
            var displacementExpression = displacementText.Length == 0 ? "0" : displacementText;
            if (!IsValueExpression(displacementExpression))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"ベース変位を解釈できません: {displacementText}");
                return null;
            }

            var indexText = baseSuppressedIndexed.Groups["index"].Value;
            var scale = baseSuppressedIndexed.Groups["scale"].Success
                ? int.Parse(baseSuppressedIndexed.Groups["scale"].Value, CultureInfo.InvariantCulture)
                : 1;
            var indexRegister = new IndexRegister(
                char.ToLowerInvariant(indexText[0]) == 'a',
                indexText[1] - '0',
                char.ToLowerInvariant(baseSuppressedIndexed.Groups["indexSize"].Value[0]) == 'l',
                scale);
            TryParseFullDisplacement(
                displacementText,
                context.Constants,
                out var baseExpression,
                out var baseSize,
                out _);
            var full = new FullIndexExtension(
                baseExpression,
                baseSize,
                "0",
                DisplacementSize.Null,
                MemoryIndirection.None,
                SuppressBase: true);
            return new EffectiveAddress(EffectiveAddressKind.Indexed, 6, 0, baseExpression, indexRegister, full);
        }

        var indexed = Indexed.Match(token);
        if (indexed.Success)
        {
            var displacementText = indexed.Groups["displacement"].Value.Trim();
            var displacementExpression = displacementText.Length == 0 ? "0" : displacementText;
            if (!IsValueExpression(displacementExpression))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"インデックス変位を解釈できません: {displacementText}");
                return null;
            }

            var indexText = indexed.Groups["index"].Value;
            var scale = indexed.Groups["scale"].Success
                ? int.Parse(indexed.Groups["scale"].Value, CultureInfo.InvariantCulture)
                : 1;
            if (scale != 1 && !RequireCpu(CpuModel.Mc68020, "スケール付きインデックス", context, statement, diagnostics))
            {
                return null;
            }

            var indexRegister = new IndexRegister(
                char.ToLowerInvariant(indexText[0]) == 'a',
                indexText[1] - '0',
                char.ToLowerInvariant(indexed.Groups["indexSize"].Value[0]) == 'l',
                scale);
            var baseText = indexed.Groups["base"].Value;
            FullIndexExtension? full = null;
            if (context.Cpu >= CpuModel.Mc68020
                && TryChooseFullBaseDisplacement(displacementExpression, context.Constants, out var baseSize))
            {
                full = new FullIndexExtension(
                    displacementExpression,
                    baseSize,
                    "0",
                    DisplacementSize.Null,
                    MemoryIndirection.None,
                    SuppressBase: false);
            }

            if (baseText.Equals("pc", StringComparison.OrdinalIgnoreCase))
            {
                return new EffectiveAddress(EffectiveAddressKind.PcIndexed, 7, 3, displacementExpression, indexRegister, full);
            }

            return new EffectiveAddress(
                EffectiveAddressKind.Indexed,
                6,
                ParseAddressRegister(baseText),
                displacementExpression,
                indexRegister,
                full);
        }

        var displacement = Displacement.Match(token);
        if (displacement.Success)
        {
            var displacementText = displacement.Groups["displacement"].Value.Trim();
            var displacementExpression = displacementText.Length == 0 ? "0" : displacementText;
            if (!IsValueExpression(displacementExpression))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"変位を解釈できません: {displacementText}");
                return null;
            }

            var baseText = displacement.Groups["base"].Value;
            if (context.Cpu >= CpuModel.Mc68020
                && TryChooseFullDisplacement16(displacementExpression, context.Constants, out var baseSize))
            {
                var full = new FullIndexExtension(
                    displacementExpression,
                    baseSize,
                    "0",
                    DisplacementSize.Null,
                    MemoryIndirection.None,
                    SuppressBase: false);
                if (baseText.Equals("pc", StringComparison.OrdinalIgnoreCase))
                {
                    return new EffectiveAddress(EffectiveAddressKind.PcIndexed, 7, 3, displacementExpression, null, full);
                }

                return new EffectiveAddress(
                    EffectiveAddressKind.Indexed,
                    6,
                    ParseAddressRegister(baseText),
                    displacementExpression,
                    null,
                    full);
            }

            if (baseText.Equals("pc", StringComparison.OrdinalIgnoreCase))
            {
                return new EffectiveAddress(EffectiveAddressKind.PcDisplacement, 7, 2, displacementExpression, null);
            }

            return new EffectiveAddress(
                EffectiveAddressKind.Displacement,
                5,
                ParseAddressRegister(baseText),
                displacementExpression,
                null);
        }

        var absoluteSize = 'l';
        var absoluteExpression = token;
        if (token.EndsWith(".w", StringComparison.OrdinalIgnoreCase)
            || token.EndsWith(".l", StringComparison.OrdinalIgnoreCase))
        {
            absoluteSize = char.ToLowerInvariant(token[^1]);
            absoluteExpression = token[..^2];
        }

        if (IsValueExpression(absoluteExpression))
        {
            return absoluteSize == 'w'
                ? new EffectiveAddress(EffectiveAddressKind.AbsoluteWord, 7, 0, absoluteExpression, null)
                : new EffectiveAddress(EffectiveAddressKind.AbsoluteLong, 7, 1, absoluteExpression, null);
        }

        AddDiagnosticOnce(context, diagnostics, statement, $"実効アドレスを解釈できません: {token}");
        return null;
    }

    private static bool TryParseFullMemoryIndirect(
        string token,
        StatementSyntax statement,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics,
        out EffectiveAddress? address)
    {
        address = null;
        if (!token.StartsWith("([", StringComparison.Ordinal)
            || !token.EndsWith(')'))
        {
            return false;
        }

        if (!RequireCpu(CpuModel.Mc68020, "メモリ間接アドレッシング", context, statement, diagnostics))
        {
            return true;
        }

        var bracketEnd = token.IndexOf(']');
        if (bracketEnd < 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"メモリ間接アドレスの']'がありません: {token}");
            return true;
        }

        var inner = token[2..bracketEnd];
        var tail = token[(bracketEnd + 1)..^1].Trim();
        if (tail.StartsWith(','))
        {
            tail = tail[1..].Trim();
        }
        else if (tail.Length != 0)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"メモリ間接アドレスの']'以降を解釈できません: {token}");
            return true;
        }

        var innerParts = SplitCommaPreservingEmpty(inner);
        var tailParts = tail.Length == 0 ? [] : SplitCommaPreservingEmpty(tail);
        if (innerParts.Count is < 1 or > 3 || tailParts.Count > 2)
        {
            AddDiagnosticOnce(context, diagnostics, statement, $"メモリ間接アドレスの要素数が不正です: {token}");
            return true;
        }

        var baseToken = innerParts.Count >= 2 ? innerParts[1].Trim() : string.Empty;
        IndexRegister? index = null;
        var indirection = MemoryIndirection.PreIndexed;
        string outerToken;

        if (innerParts.Count == 3)
        {
            if (!TryParseIndexRegister(innerParts[2], out index))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"プリインデックスレジスタを解釈できません: {innerParts[2]}");
                return true;
            }

            if (tailParts.Count > 1)
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"プリインデックス形式の外側変位は1つです: {token}");
                return true;
            }

            outerToken = tailParts.Count == 0 ? string.Empty : tailParts[0];
        }
        else if (tailParts.Count > 0 && TryParseIndexRegister(tailParts[0], out index))
        {
            indirection = MemoryIndirection.PostIndexed;
            outerToken = tailParts.Count == 2 ? tailParts[1] : string.Empty;
        }
        else
        {
            // Index-suppressed pre-indexed form: ([bd,An],od)
            outerToken = tailParts.Count == 0 ? string.Empty : tailParts[0];
        }

        var suppressBase = baseToken.Length == 0;
        var pcRelative = false;
        var baseRegister = 0;
        if (!suppressBase)
        {
            if (baseToken.Equals("pc", StringComparison.OrdinalIgnoreCase))
            {
                pcRelative = true;
            }
            else if (!TryParseAddressRegisterToken(baseToken, out baseRegister))
            {
                AddDiagnosticOnce(context, diagnostics, statement, $"ベースレジスタを解釈できません: {baseToken}");
                return true;
            }
        }

        if (!TryParseFullDisplacement(
            innerParts[0],
            context.Constants,
            out var baseExpression,
            out var baseSize,
            out var baseError))
        {
            AddDiagnosticOnce(context, diagnostics, statement, baseError);
            return true;
        }

        if (!TryParseFullDisplacement(
            outerToken,
            context.Constants,
            out var outerExpression,
            out var outerSize,
            out var outerError))
        {
            AddDiagnosticOnce(context, diagnostics, statement, outerError);
            return true;
        }

        var full = new FullIndexExtension(
            baseExpression,
            baseSize,
            outerExpression,
            outerSize,
            indirection,
            suppressBase);
        address = pcRelative
            ? new EffectiveAddress(EffectiveAddressKind.PcIndexed, 7, 3, baseExpression, index, full)
            : new EffectiveAddress(EffectiveAddressKind.Indexed, 6, baseRegister, baseExpression, index, full);
        return true;
    }

    private static List<string> SplitCommaPreservingEmpty(string text) =>
        text.Split(',', StringSplitOptions.None).Select(item => item.Trim()).ToList();

    private static bool TryParseIndexRegister(string token, out IndexRegister? index)
    {
        index = null;
        var match = Regex.Match(
            token.Trim(),
            @"^(?<kind>[dDaA])(?<register>[0-7])(?:\.(?<size>[wWlL]))?(?:\s*\*\s*(?<scale>[1248]))?$");
        if (!match.Success)
        {
            return false;
        }

        var scale = match.Groups["scale"].Success
            ? int.Parse(match.Groups["scale"].Value, CultureInfo.InvariantCulture)
            : 1;
        index = new IndexRegister(
            char.ToLowerInvariant(match.Groups["kind"].Value[0]) == 'a',
            match.Groups["register"].Value[0] - '0',
            match.Groups["size"].Success
                && char.ToLowerInvariant(match.Groups["size"].Value[0]) == 'l',
            scale);
        return true;
    }

    private static bool TryParseFullDisplacement(
        string token,
        IReadOnlyDictionary<string, long> constants,
        out string expression,
        out DisplacementSize size,
        out string error)
    {
        expression = "0";
        size = DisplacementSize.Null;
        error = string.Empty;
        var valueToken = token.Trim();
        if (valueToken.Length == 0)
        {
            return true;
        }

        DisplacementSize? forcedSize = null;
        if (valueToken.EndsWith(".w", StringComparison.OrdinalIgnoreCase)
            || valueToken.EndsWith(".l", StringComparison.OrdinalIgnoreCase))
        {
            forcedSize = char.ToLowerInvariant(valueToken[^1]) == 'w'
                ? DisplacementSize.Word
                : DisplacementSize.Long;
            valueToken = valueToken[..^2].TrimEnd();
        }

        if (!IsValueExpression(valueToken))
        {
            error = $"変位式を解釈できません: {token}";
            return false;
        }

        expression = valueToken;
        if (forcedSize is not null)
        {
            size = forcedSize.Value;
            return true;
        }

        if (TryEvaluateConstant(valueToken, constants, out var value, out _))
        {
            size = value is >= short.MinValue and <= short.MaxValue
                ? DisplacementSize.Word
                : DisplacementSize.Long;
        }
        else
        {
            // Symbolic displacements stay layout-stable by using the longest form.
            size = DisplacementSize.Long;
        }

        return true;
    }

    private static bool TryChooseFullBaseDisplacement(
        string expression,
        IReadOnlyDictionary<string, long> constants,
        out DisplacementSize size)
    {
        if (TryEvaluateConstant(expression, constants, out var value, out _))
        {
            if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
            {
                size = DisplacementSize.Null;
                return false;
            }

            size = value is >= short.MinValue and <= short.MaxValue
                ? DisplacementSize.Word
                : DisplacementSize.Long;
            return true;
        }

        size = DisplacementSize.Long;
        return true;
    }

    private static bool TryChooseFullDisplacement16(
        string expression,
        IReadOnlyDictionary<string, long> constants,
        out DisplacementSize size)
    {
        if (TryEvaluateConstant(expression, constants, out var value, out _))
        {
            if (value is >= short.MinValue and <= short.MaxValue)
            {
                size = DisplacementSize.Null;
                return false;
            }

            size = DisplacementSize.Long;
            return true;
        }

        size = DisplacementSize.Long;
        return true;
    }

    private static void WriteEffectiveAddressExtensions(
        EffectiveAddress address,
        char size,
        string procedureName,
        StatementSyntax statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var extensionBytes = ExtensionSize(address, size);
        if (extensionBytes == 0)
        {
            return;
        }

        if (context.Pass == AssemblyPass.Layout)
        {
            if (address.Full is not null)
            {
                ValidateFullIndexExpressions(address.Full, procedureName, statement, context, diagnostics);
            }
            else if (address.Expression is not null
                && !TryResolveExpression(address.Expression, procedureName, statement, context, out _, out var layoutError))
            {
                diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, layoutError));
            }

            writer.WriteZeroes(extensionBytes);
            return;
        }

        if (address.Full is not null)
        {
            WriteFullIndexExtension(address, procedureName, statement, writer, context, diagnostics);
            return;
        }

        if (address.Expression is null)
        {
            throw new InvalidOperationException("拡張ワードを持つ実効アドレスに式がありません");
        }

        if (!TryResolveExpression(address.Expression, procedureName, statement, context, out var value, out var error))
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, error));
            writer.WriteZeroes(extensionBytes);
            return;
        }

        switch (address.Kind)
        {
            case EffectiveAddressKind.Immediate:
                if (!FitsSize(value, size))
                {
                    diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"即値が.{size}の範囲外です: {value}"));
                    writer.WriteZeroes(extensionBytes);
                    return;
                }

                WriteImmediate(value, size, writer);
                break;
            case EffectiveAddressKind.Displacement:
                WriteSignedDisplacement(value, 16, statement, writer, diagnostics);
                break;
            case EffectiveAddressKind.Indexed:
                WriteBriefIndexExtension(value, address.Index, statement, writer, diagnostics);
                break;
            case EffectiveAddressKind.AbsoluteWord:
                if (value is < short.MinValue or > ushort.MaxValue)
                {
                    diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"絶対ワードアドレスが範囲外です: {value}"));
                    writer.WriteWord(0);
                    return;
                }

                writer.WriteWord(unchecked((ushort)value));
                break;
            case EffectiveAddressKind.AbsoluteLong:
                if (value is < int.MinValue or > uint.MaxValue)
                {
                    diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"絶対ロングアドレスが範囲外です: {value}"));
                    writer.WriteLong(0);
                    return;
                }

                writer.WriteLong(unchecked((uint)value));
                break;
            case EffectiveAddressKind.PcDisplacement:
                {
                    var pc = (long)context.BaseAddress + writer.Position;
                    WriteSignedDisplacement(value - pc, 16, statement, writer, diagnostics);
                    break;
                }
            case EffectiveAddressKind.PcIndexed:
                {
                    var pc = (long)context.BaseAddress + writer.Position;
                    WriteBriefIndexExtension(value - pc, address.Index, statement, writer, diagnostics);
                    break;
                }
        }
    }

    private static void WriteSignedDisplacement(
        long value,
        int bits,
        StatementSyntax statement,
        BigEndianWriter writer,
        List<SourceDiagnostic> diagnostics)
    {
        var minimum = -(1L << (bits - 1));
        var maximum = (1L << (bits - 1)) - 1;
        if (value < minimum || value > maximum)
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"{bits}ビット変位が範囲外です: {value}"));
            writer.WriteWord(0);
            return;
        }

        writer.WriteWord(unchecked((ushort)(short)value));
    }

    private static void WriteBriefIndexExtension(
        long displacement,
        IndexRegister? index,
        StatementSyntax statement,
        BigEndianWriter writer,
        List<SourceDiagnostic> diagnostics)
    {
        if (index is null)
        {
            throw new InvalidOperationException("インデックスレジスタがありません");
        }

        if (displacement is < sbyte.MinValue or > sbyte.MaxValue)
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"インデックス変位が8ビット範囲外です: {displacement}"));
            writer.WriteWord(0);
            return;
        }

        var extension = (index.IsAddress ? 0x8000 : 0)
            | (index.Register << 12)
            | (index.IsLong ? 0x0800 : 0)
            | (ScaleBits(index.Scale) << 9)
            | (byte)(sbyte)displacement;
        writer.WriteWord((ushort)extension);
    }

    private static void ValidateFullIndexExpressions(
        FullIndexExtension full,
        string procedureName,
        StatementSyntax statement,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        if (!TryResolveExpression(full.BaseExpression, procedureName, statement, context, out _, out var baseError))
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, baseError));
        }

        if (!TryResolveExpression(full.OuterExpression, procedureName, statement, context, out _, out var outerError))
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, outerError));
        }
    }

    private static void WriteFullIndexExtension(
        EffectiveAddress address,
        string procedureName,
        StatementSyntax statement,
        BigEndianWriter writer,
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics)
    {
        var full = address.Full ?? throw new InvalidOperationException("フル拡張ワードがありません");
        var resolved = true;
        if (!TryResolveExpression(full.BaseExpression, procedureName, statement, context, out var baseValue, out var baseError))
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, baseError));
            baseValue = 0;
            resolved = false;
        }

        if (!TryResolveExpression(full.OuterExpression, procedureName, statement, context, out var outerValue, out var outerError))
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, outerError));
            outerValue = 0;
            resolved = false;
        }

        var extensionAddress = (long)context.BaseAddress + writer.Position;
        if (address.Kind == EffectiveAddressKind.PcIndexed && !full.SuppressBase)
        {
            baseValue -= extensionAddress;
        }

        var extension = 0x0100
            | (full.SuppressBase ? 0x0080 : 0)
            | (address.Index is null ? 0x0040 : 0)
            | (DisplacementCode(full.BaseSize) << 4)
            | IndirectionCode(full.Indirection, full.OuterSize);
        if (address.Index is not null)
        {
            extension |= (address.Index.IsAddress ? 0x8000 : 0)
                | (address.Index.Register << 12)
                | (address.Index.IsLong ? 0x0800 : 0)
                | (ScaleBits(address.Index.Scale) << 9);
        }

        writer.WriteWord((ushort)extension);
        if (!resolved)
        {
            writer.WriteZeroes(DisplacementBytes(full.BaseSize) + DisplacementBytes(full.OuterSize));
            return;
        }

        WriteFullDisplacement(baseValue, full.BaseSize, "ベース", statement, writer, diagnostics);
        WriteFullDisplacement(outerValue, full.OuterSize, "外側", statement, writer, diagnostics);
    }

    private static void WriteFullDisplacement(
        long value,
        DisplacementSize size,
        string name,
        StatementSyntax statement,
        BigEndianWriter writer,
        List<SourceDiagnostic> diagnostics)
    {
        switch (size)
        {
            case DisplacementSize.Null:
                return;
            case DisplacementSize.Word:
                if (value is < short.MinValue or > short.MaxValue)
                {
                    diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"{name}変位が16ビット範囲外です: {value}"));
                    writer.WriteWord(0);
                    return;
                }

                writer.WriteWord(unchecked((ushort)(short)value));
                return;
            case DisplacementSize.Long:
                if (value is < int.MinValue or > int.MaxValue)
                {
                    diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, $"{name}変位が32ビット範囲外です: {value}"));
                    writer.WriteLong(0);
                    return;
                }

                writer.WriteLong(unchecked((uint)(int)value));
                return;
            default:
                throw new InvalidOperationException($"未知の変位幅: {size}");
        }
    }

    private static int ScaleBits(int scale) => scale switch
    {
        1 => 0,
        2 => 1,
        4 => 2,
        8 => 3,
        _ => throw new InvalidOperationException($"不正なインデックススケール: {scale}"),
    };

    private static int DisplacementCode(DisplacementSize size) => size switch
    {
        DisplacementSize.Null => 1,
        DisplacementSize.Word => 2,
        DisplacementSize.Long => 3,
        _ => throw new InvalidOperationException($"未知の変位幅: {size}"),
    };

    private static int IndirectionCode(MemoryIndirection indirection, DisplacementSize outerSize) => indirection switch
    {
        MemoryIndirection.None => 0,
        MemoryIndirection.PreIndexed => outerSize switch
        {
            DisplacementSize.Null => 1,
            DisplacementSize.Word => 2,
            DisplacementSize.Long => 3,
            _ => throw new InvalidOperationException(),
        },
        MemoryIndirection.PostIndexed => outerSize switch
        {
            DisplacementSize.Null => 5,
            DisplacementSize.Word => 6,
            DisplacementSize.Long => 7,
            _ => throw new InvalidOperationException(),
        },
        _ => throw new InvalidOperationException($"未知のメモリ間接種別: {indirection}"),
    };

    private static int DisplacementBytes(DisplacementSize size) => size switch
    {
        DisplacementSize.Null => 0,
        DisplacementSize.Word => 2,
        DisplacementSize.Long => 4,
        _ => throw new InvalidOperationException($"未知の変位幅: {size}"),
    };

    private static int ExtensionSize(EffectiveAddress address, char size) => address.Kind switch
    {
        EffectiveAddressKind.Displacement => 2,
        EffectiveAddressKind.Indexed => address.Full?.ExtensionBytes ?? 2,
        EffectiveAddressKind.AbsoluteWord => 2,
        EffectiveAddressKind.AbsoluteLong => 4,
        EffectiveAddressKind.PcDisplacement => 2,
        EffectiveAddressKind.PcIndexed => address.Full?.ExtensionBytes ?? 2,
        EffectiveAddressKind.Immediate => size == 'l' ? 4 : 2,
        _ => 0,
    };

    private static bool IsControlAddress(EffectiveAddress address) => address.Kind is
        EffectiveAddressKind.Indirect
        or EffectiveAddressKind.Displacement
        or EffectiveAddressKind.Indexed
        or EffectiveAddressKind.AbsoluteWord
        or EffectiveAddressKind.AbsoluteLong
        or EffectiveAddressKind.PcDisplacement
        or EffectiveAddressKind.PcIndexed;

    private static bool IsMemoryAlterable(EffectiveAddress address) => address.Kind is
        EffectiveAddressKind.Indirect
        or EffectiveAddressKind.PostIncrement
        or EffectiveAddressKind.PreDecrement
        or EffectiveAddressKind.Displacement
        or EffectiveAddressKind.Indexed
        or EffectiveAddressKind.AbsoluteWord
        or EffectiveAddressKind.AbsoluteLong;

    private static bool IsDataAlterable(EffectiveAddress address) =>
        address.Kind == EffectiveAddressKind.DataRegister || IsMemoryAlterable(address);

    private static bool IsDataAddress(EffectiveAddress address) =>
        IsDataAlterable(address)
        || address.Kind is
            EffectiveAddressKind.PcDisplacement
            or EffectiveAddressKind.PcIndexed
            or EffectiveAddressKind.Immediate;

    private static int EncodeEffectiveAddress(EffectiveAddress address) =>
        (address.Mode << 3) | address.Register;

    private static int ParseAddressRegister(string text)
    {
        var token = text.Trim();
        if (token.StartsWith("sp", StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        return token[1] - '0';
    }

    private static bool TryResolveExpression(
        string expression,
        string procedureName,
        StatementSyntax statement,
        AssemblyContext context,
        out long value,
        out string error)
    {
        return ExpressionEvaluator.TryEvaluate(
            expression,
            name => context.ResolveSymbol(name, procedureName, statement),
            out value,
            out error);
    }

    private static bool IsValueExpression(string text) => ExpressionEvaluator.IsValid(text.Trim());

    private static bool TryParseNumber(string text, out long value)
    {
        value = 0;
        var token = text.Trim();
        var negative = token.StartsWith('-');
        var positive = token.StartsWith('+');
        if (negative || positive)
        {
            token = token[1..];
        }

        try
        {
            long magnitude;
            if (token.StartsWith('$'))
            {
                magnitude = long.Parse(token[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            }
            else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                magnitude = long.Parse(token[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            }
            else if (token.StartsWith('%'))
            {
                magnitude = Convert.ToInt64(token[1..], 2);
            }
            else
            {
                magnitude = long.Parse(token, NumberStyles.None, CultureInfo.InvariantCulture);
            }

            value = negative ? -magnitude : magnitude;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryEvaluateConstant(
        string expression,
        IReadOnlyDictionary<string, long> constants,
        out long value,
        out string error) =>
        ExpressionEvaluator.TryEvaluate(
            expression,
            name => constants.TryGetValue(name, out var constant)
                ? SymbolResolution.Resolved(constant)
                : SymbolResolution.Failed($"未定義の定数ラベルです: {name}"),
            out value,
            out error);

    private static bool TryParseByteString(string text, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        var token = text.Trim();
        if (token.Length < 2 || token[0] != '"' || token[^1] != '"')
        {
            error = $"文字列はダブルクォートで囲みます: {text}";
            return false;
        }

        var result = new List<byte>();
        for (var index = 1; index < token.Length - 1; index++)
        {
            var current = token[index];
            if (current != '\\')
            {
                if (current > byte.MaxValue)
                {
                    error = $"1バイトに収まらない文字です。\\xHHを使用してください: {current}";
                    return false;
                }

                result.Add((byte)current);
                continue;
            }

            if (++index >= token.Length - 1)
            {
                error = "文字列末尾のエスケープが不完全です";
                return false;
            }

            var escaped = token[index];
            switch (escaped)
            {
                case '0':
                    result.Add(0);
                    break;
                case 'n':
                    result.Add((byte)'\n');
                    break;
                case 'r':
                    result.Add((byte)'\r');
                    break;
                case 't':
                    result.Add((byte)'\t');
                    break;
                case '\\':
                    result.Add((byte)'\\');
                    break;
                case '"':
                    result.Add((byte)'"');
                    break;
                case 'x':
                    if (index + 2 >= token.Length
                        || !byte.TryParse(
                            token.AsSpan(index + 1, 2),
                            NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture,
                            out var hexadecimal))
                    {
                        error = "\\xには2桁の16進数を指定します";
                        return false;
                    }

                    result.Add(hexadecimal);
                    index += 2;
                    break;
                default:
                    error = $"未知の文字列エスケープです: \\{escaped}";
                    return false;
            }
        }

        bytes = result.ToArray();
        return true;
    }

    private static bool TryParseSizedNumber(string text, char size, out long value, out string error)
    {
        error = string.Empty;
        if (!TryParseNumber(text, out value))
        {
            error = $"即値 '#{text}' を数値として解釈できません";
            return false;
        }

        if (!FitsSize(value, size))
        {
            error = $"即値 '#{text}' は.{size}の範囲外です";
            return false;
        }

        return true;
    }

    private static bool FitsSize(long value, char size) => size switch
    {
        'b' => value is >= sbyte.MinValue and <= byte.MaxValue,
        'w' => value is >= short.MinValue and <= ushort.MaxValue,
        'l' => value is >= int.MinValue and <= uint.MaxValue,
        _ => false,
    };

    private static void AddSymbol(
        string name,
        string procedureName,
        int scopeId,
        int position,
        AssemblyContext context,
        int line,
        int column,
        List<SourceDiagnostic> diagnostics)
    {
        if (!name.StartsWith('.') && context.Constants.ContainsKey(name))
        {
            diagnostics.Add(new SourceDiagnostic(line, column, $"シンボル '{name}' は同名の定数ラベルと競合します"));
            return;
        }

        var qualified = QualifySymbol(name, procedureName, scopeId);
        var absolute = (ulong)context.BaseAddress + (uint)position;
        if (absolute > uint.MaxValue)
        {
            diagnostics.Add(new SourceDiagnostic(line, column, $"シンボルアドレスが32ビット範囲を超えています: {name}"));
            return;
        }

        if (!context.Symbols.TryAdd(qualified, (uint)absolute))
        {
            diagnostics.Add(new SourceDiagnostic(line, column, $"シンボル '{name}' はすでに定義されています"));
        }
    }

    private static string QualifySymbol(string name, string procedureName) =>
        name.StartsWith('.') ? $"{procedureName}{name}" : name;

    private static string QualifySymbol(string name, string procedureName, int scopeId) =>
        scopeId == 0
            ? QualifySymbol(name, procedureName)
            : $"{procedureName}::scope{scopeId}::{name.TrimStart('.')}";

    private static void AddDiagnosticOnce(
        AssemblyContext context,
        List<SourceDiagnostic> diagnostics,
        StatementSyntax statement,
        string message)
    {
        if (context.Pass == AssemblyPass.Layout)
        {
            diagnostics.Add(new SourceDiagnostic(statement.Line, statement.Column, message));
        }
    }

    private static List<string> SplitOperands(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var builder = new StringBuilder();
        var depth = 0;
        var inString = false;
        var escaped = false;
        foreach (var current in text)
        {
            if (current == '"' && !escaped)
            {
                inString = !inString;
            }

            if (inString)
            {
                builder.Append(current);
                escaped = current == '\\' && !escaped;
                if (current != '\\')
                {
                    escaped = false;
                }

                continue;
            }

            switch (current)
            {
                case '(':
                    depth++;
                    builder.Append(current);
                    break;
                case ')':
                    depth--;
                    builder.Append(current);
                    break;
                case ',' when depth == 0:
                    result.Add(builder.ToString().Trim());
                    builder.Clear();
                    break;
                default:
                    builder.Append(current);
                    break;
            }

            escaped = false;
        }

        if (depth != 0 || inString)
        {
            return [];
        }

        result.Add(builder.ToString().Trim());
        return result.Where(item => item.Length != 0).ToList();
    }

    private static bool TryParseInstructionSize(string suffix, out char size)
    {
        size = suffix.Length == 1 ? suffix[0] : '\0';
        return size is 'b' or 'w' or 'l';
    }

    private static bool SameStorage(AlgebraOperand left, AlgebraOperand right) =>
        left.Kind == right.Kind
        && left.Size == right.Size
        && left.Register == right.Register
        && left.Offset == right.Offset
        && left.Kind != AlgebraOperandKind.Immediate;

    private static bool IsMemoryOperand(AlgebraOperand operand) =>
        operand.Kind is AlgebraOperandKind.Local or AlgebraOperandKind.StructureField;

    private static int EncodeAlgebraEffectiveAddress(AlgebraOperand operand) => operand.Kind switch
    {
        AlgebraOperandKind.DataRegister => operand.Register,
        AlgebraOperandKind.Local or AlgebraOperandKind.StructureField => (5 << 3) | operand.Register,
        AlgebraOperandKind.Immediate => (7 << 3) | 4,
        _ => throw new InvalidOperationException($"未知のオペランド種別: {operand.Kind}"),
    };

    private static int EncodeAlgebraMoveDestination(AlgebraOperand operand) => operand.Kind switch
    {
        AlgebraOperandKind.DataRegister => operand.Register << 9,
        AlgebraOperandKind.Local or AlgebraOperandKind.StructureField => (operand.Register << 9) | (5 << 6),
        _ => throw new InvalidOperationException("MOVEの出力先として利用できません"),
    };

    private static int MoveBase(char size) => size switch
    {
        'b' => 0x1000,
        'w' => 0x3000,
        'l' => 0x2000,
        _ => throw new InvalidOperationException($"未知の幅: {size}"),
    };

    private static int SizeBits(char size) => size switch
    {
        'b' => 0x0000,
        'w' => 0x0040,
        'l' => 0x0080,
        _ => throw new InvalidOperationException($"未知の幅: {size}"),
    };

    private static void WriteAlgebraExtension(AlgebraOperand operand, BigEndianWriter writer)
    {
        switch (operand.Kind)
        {
            case AlgebraOperandKind.Local:
            case AlgebraOperandKind.StructureField:
                writer.WriteWord(unchecked((ushort)operand.Offset));
                break;
            case AlgebraOperandKind.Immediate:
                WriteImmediate(operand.Value, operand.Size, writer);
                break;
        }
    }

    private static void WriteImmediate(long value, char size, BigEndianWriter writer)
    {
        if (size == 'l')
        {
            writer.WriteLong(unchecked((uint)value));
            return;
        }

        writer.WriteWord(unchecked((ushort)value));
    }

    private static char TypeToSize(string type) => type switch
    {
        "byte" => 'b',
        "word" => 'w',
        "long" or "ptr" => 'l',
        _ => throw new InvalidOperationException($"未知の型: {type}"),
    };

    private static int SizeInBytes(char size) => size switch
    {
        'b' => 1,
        'w' => 2,
        'l' => 4,
        _ => throw new InvalidOperationException($"未知の幅: {size}"),
    };

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;

    private enum AssemblyPass
    {
        Layout,
        Emit,
    }

    private enum AlgebraOperandKind
    {
        DataRegister,
        Local,
        StructureField,
        Immediate,
    }

    private enum EffectiveAddressKind
    {
        DataRegister,
        AddressRegister,
        Indirect,
        PostIncrement,
        PreDecrement,
        Displacement,
        Indexed,
        AbsoluteWord,
        AbsoluteLong,
        PcDisplacement,
        PcIndexed,
        Immediate,
    }

    private readonly record struct AlgebraOperand(
        AlgebraOperandKind Kind,
        int Register,
        short Offset,
        long Value,
        char Size)
    {
        public static AlgebraOperand DataRegister(int register, char size) =>
            new(AlgebraOperandKind.DataRegister, register, 0, 0, size);

        public static AlgebraOperand Local(short offset, char size) =>
            new(AlgebraOperandKind.Local, 6, offset, 0, size);

        public static AlgebraOperand StructureField(int baseRegister, short offset, char size) =>
            new(AlgebraOperandKind.StructureField, baseRegister, offset, 0, size);

        public static AlgebraOperand Immediate(long value, char size) =>
            new(AlgebraOperandKind.Immediate, 0, 0, value, size);
    }

    private readonly record struct EffectiveAddress(
        EffectiveAddressKind Kind,
        int Mode,
        int Register,
        string? Expression,
        IndexRegister? Index,
        FullIndexExtension? Full = null);

    private sealed record IndexRegister(bool IsAddress, int Register, bool IsLong, int Scale = 1);

    private enum DisplacementSize
    {
        Null,
        Word,
        Long,
    }

    private enum MemoryIndirection
    {
        None,
        PreIndexed,
        PostIndexed,
    }

    private sealed record FullIndexExtension(
        string BaseExpression,
        DisplacementSize BaseSize,
        string OuterExpression,
        DisplacementSize OuterSize,
        MemoryIndirection Indirection,
        bool SuppressBase)
    {
        public int ExtensionBytes =>
            2 + DisplacementBytes(BaseSize) + DisplacementBytes(OuterSize);
    }

    private sealed record LocalSlot(char Size, short Offset);

    private sealed record LocalLayout(
        IReadOnlyDictionary<string, LocalSlot> Slots,
        int FrameSize);

    private sealed class SymbolDeclarations
    {
        private readonly Dictionary<string, ProcedureSyntax> _procedures;
        private readonly HashSet<string> _declared = new(StringComparer.OrdinalIgnoreCase);

        public SymbolDeclarations(ModuleSyntax module)
        {
            _procedures = module.Procedures.ToDictionary(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase);
            foreach (var procedure in module.Procedures)
            {
                _declared.Add(procedure.Name);
                foreach (var statement in procedure.Statements.OfType<RawStatement>())
                {
                    var label = Label.Match(statement.Text.Trim());
                    if (label.Success)
                    {
                        _declared.Add(QualifySymbol(label.Groups["name"].Value, procedure.Name, statement.ScopeId));
                    }
                }
            }
        }

        public bool TryResolveKey(string name, string procedureName, int scopeId, out string key)
        {
            if (scopeId != 0 && _procedures.TryGetValue(procedureName, out var procedure))
            {
                int? current = scopeId;
                while (current is > 0)
                {
                    var scoped = QualifySymbol(name, procedureName, current.Value);
                    if (_declared.Contains(scoped))
                    {
                        key = scoped;
                        return true;
                    }

                    current = procedure.ScopeParents[current.Value];
                }
            }

            var root = QualifySymbol(name, procedureName, 0);
            if (_declared.Contains(root))
            {
                key = root;
                return true;
            }

            key = string.Empty;
            return false;
        }
    }

    private sealed class AssemblyContext
    {
        public AssemblyContext(
            AssemblyPass pass,
            uint baseAddress,
            CpuModel cpu,
            Dictionary<string, uint> symbols,
            SymbolDeclarations declarations,
            IReadOnlyDictionary<string, long> constants)
        {
            Pass = pass;
            BaseAddress = baseAddress;
            Cpu = cpu;
            Symbols = symbols;
            Declarations = declarations;
            Constants = constants;
        }

        public AssemblyPass Pass { get; }
        public uint BaseAddress { get; }
        public CpuModel Cpu { get; }
        public Dictionary<string, uint> Symbols { get; }
        public SymbolDeclarations Declarations { get; }
        public IReadOnlyDictionary<string, long> Constants { get; }

        public SymbolResolution ResolveSymbol(string name, string procedureName, StatementSyntax statement)
        {
            if (Constants.TryGetValue(name, out var constant))
            {
                return SymbolResolution.Resolved(constant);
            }

            if (Declarations.TryResolveKey(name, procedureName, statement.ScopeId, out var key))
            {
                if (Pass == AssemblyPass.Layout)
                {
                    return SymbolResolution.Resolved(0);
                }

                return Symbols.TryGetValue(key, out var value)
                    ? SymbolResolution.Resolved(value)
                    : SymbolResolution.Failed($"未定義シンボルです: {name}");
            }

            return SymbolResolution.Failed($"未定義シンボルです: {name}");
        }
    }

    private sealed record StructureFieldLayout(char Size, short Offset);

    private sealed record StructureLayout(
        int? BaseRegister,
        IReadOnlyDictionary<string, StructureFieldLayout> Fields,
        int Size);

    private sealed class BigEndianWriter
    {
        private readonly List<byte> _bytes = new();

        public int Position => _bytes.Count;

        public void WriteByte(byte value) => _bytes.Add(value);

        public void WriteWord(ushort value)
        {
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }

        public void WriteLong(uint value)
        {
            WriteWord((ushort)(value >> 16));
            WriteWord((ushort)value);
        }

        public void WriteZeroes(int count)
        {
            for (var index = 0; index < count; index++)
            {
                _bytes.Add(0);
            }
        }

        public void WriteBytes(IEnumerable<byte> values) => _bytes.AddRange(values);

        public void Align(int alignment, byte fill = 0)
        {
            while ((_bytes.Count % alignment) != 0)
            {
                _bytes.Add(fill);
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}
