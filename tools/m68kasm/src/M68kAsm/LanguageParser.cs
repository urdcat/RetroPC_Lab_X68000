using System.Text;
using System.Text.RegularExpressions;

namespace M68kAsm;

public sealed class LanguageParser
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex ProcedureStart = new("^\\[([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);
    private static readonly Regex StructureStart = new(
        "^struct(?:\\s+base\\s*:\\s*(?<base>[aA][0-7]|[sS][pP]))?\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\{$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Declaration = new("^([A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*(byte|word|long|ptr)$", RegexOptions.Compiled);
    private static readonly Regex ShortFieldDeclaration = new(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\.(?<size>[bBwWlL])$",
        RegexOptions.Compiled);
    private static readonly Regex Algebra = new(
        "^((?:[dD][0-7]\\.(?<size>[bBwWlL]))|(?:[aA][0-7](?:\\.[lL])?)|(?:[A-Za-z_][A-Za-z0-9_]*)(?:\\s*,\\s*[A-Za-z_][A-Za-z0-9_]*)?)\\s*(=|\\+=|-=)\\s*(.+)$",
        RegexOptions.Compiled);
    private static readonly Regex DataRegister = new("(?<![A-Za-z0-9_])[dD][0-7](?:\\.(?<size>[bBwWlL]))?(?![A-Za-z0-9_])", RegexOptions.Compiled);
    private static readonly Regex DataRegisterAtStart = new("^[dD][0-7](?:\\b|\\.)", RegexOptions.Compiled);
    private static readonly Regex BitFieldInstructionAtStart = new(
        @"^(?:bfchg|bfclr|bfexts|bfextu|bfffo|bfins|bfset|bftst)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ParseResult Parse(string source) => Parse(source, sourcePath: null);

    public ParseResult Parse(string source, string? sourcePath)
    {
        var preprocessed = new SourcePreprocessor().Process(source, sourcePath);
        return ParseCore(preprocessed.Source, preprocessed.Constants, preprocessed.Diagnostics);
    }

    private static ParseResult ParseCore(
        string source,
        IReadOnlyDictionary<string, long> constants,
        IReadOnlyList<SourceDiagnostic> preprocessingDiagnostics)
    {
        var module = new ModuleSyntax(constants);
        var diagnostics = new List<SourceDiagnostic>(preprocessingDiagnostics);
        ProcedureSyntax? procedure = null;
        StructureSyntax? structure = null;
        var scopes = new Stack<int>();
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            foreach (var part in SplitStatements(lines[index]))
            {
                var text = part.Text.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var column = part.Column + part.Text.Length - part.Text.TrimStart().Length;

                if (structure is not null)
                {
                    ParseStructureStatement(text, structure, lineNumber, column, diagnostics, ref structure);
                    continue;
                }

                if (procedure is not null)
                {
                    if (text == "{")
                    {
                        scopes.Push(procedure.CreateScope(scopes.Peek()));
                        continue;
                    }

                    if (text == "}")
                    {
                        if (scopes.Count == 1)
                        {
                            diagnostics.Add(new SourceDiagnostic(lineNumber, column, "対応する'{'がない'}'です"));
                        }
                        else
                        {
                            scopes.Pop();
                        }

                        continue;
                    }

                    if (text is "]" or "]/" && scopes.Count != 1)
                    {
                        diagnostics.Add(new SourceDiagnostic(lineNumber, column, "複文を'}'で閉じてから手続きを終了してください"));
                        continue;
                    }

                    ParseProcedureStatement(
                        text,
                        procedure,
                        scopes.Peek(),
                        lineNumber,
                        column,
                        diagnostics,
                        ref procedure,
                        module);
                    if (procedure is null)
                    {
                        scopes.Clear();
                    }

                    continue;
                }

                if (text == "}" || text == "]" || text == "]/" || text.StartsWith("local ", StringComparison.Ordinal))
                {
                    diagnostics.Add(new SourceDiagnostic(lineNumber, column, "手続きまたは構造体の外に閉じ記号・localがあります"));
                    continue;
                }

                var structureMatch = StructureStart.Match(text);
                if (structureMatch.Success)
                {
                    var name = structureMatch.Groups["name"].Value;
                    if (module.Constants.ContainsKey(name))
                    {
                        diagnostics.Add(new SourceDiagnostic(lineNumber, column, $"構造体 '{name}' は同名の定数ラベルと競合します"));
                    }
                    else if (module.Structures.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        diagnostics.Add(new SourceDiagnostic(lineNumber, column, $"構造体 '{name}' はすでに定義されています"));
                    }
                    else
                    {
                        var baseRegister = structureMatch.Groups["base"].Success
                            ? (int?)ParseAddressRegister(structureMatch.Groups["base"].Value)
                            : null;
                        structure = new StructureSyntax(name, lineNumber, baseRegister);
                        module.Structures.Add(structure);
                    }

                    continue;
                }

                var procedureMatch = ProcedureStart.Match(text);
                if (procedureMatch.Success)
                {
                    var name = procedureMatch.Groups[1].Value;
                    if (module.Constants.ContainsKey(name))
                    {
                        diagnostics.Add(new SourceDiagnostic(lineNumber, column, $"手続き '{name}' は同名の定数ラベルと競合します"));
                    }
                    else if (module.Procedures.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        diagnostics.Add(new SourceDiagnostic(lineNumber, column, $"手続き '{name}' はすでに定義されています"));
                    }
                    else
                    {
                        procedure = new ProcedureSyntax(name, lineNumber);
                        scopes.Push(0);
                    }

                    continue;
                }

                diagnostics.Add(new SourceDiagnostic(lineNumber, column, $"トップレベルの文として認識できません: {text}"));
            }
        }

        if (structure is not null)
        {
            diagnostics.Add(new SourceDiagnostic(structure.Line, 1, $"構造体 '{structure.Name}' が '}}' で閉じられていません"));
        }

        if (procedure is not null)
        {
            diagnostics.Add(new SourceDiagnostic(procedure.Line, 1, $"手続き '{procedure.Name}' が ']' または ']/' で閉じられていません"));
        }

        return new ParseResult(module, diagnostics);
    }

    private static void ParseStructureStatement(
        string text,
        StructureSyntax current,
        int line,
        int column,
        List<SourceDiagnostic> diagnostics,
        ref StructureSyntax? structure)
    {
        if (text == "}")
        {
            structure = null;
            return;
        }

        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var declaration = Declaration.Match(part);
            var shortDeclaration = ShortFieldDeclaration.Match(part);
            if (!declaration.Success && !shortDeclaration.Success)
            {
                diagnostics.Add(new SourceDiagnostic(
                    line,
                    column,
                    "構造体フィールドは '名前: byte|word|long|ptr' または '名前.b|.w|.l' で記述します"));
                continue;
            }

            var name = declaration.Success
                ? declaration.Groups[1].Value
                : shortDeclaration.Groups["name"].Value;
            var type = declaration.Success
                ? declaration.Groups[2].Value
                : shortDeclaration.Groups["size"].Value.ToLowerInvariant() switch
                {
                    "b" => "byte",
                    "w" => "word",
                    "l" => "long",
                    _ => throw new InvalidOperationException(),
                };
            if (current.Fields.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new SourceDiagnostic(line, column, $"フィールド '{name}' はすでに定義されています"));
                continue;
            }

            current.Fields.Add(new FieldSyntax(name, type, line, column));
        }
    }

    private static void ParseProcedureStatement(
        string text,
        ProcedureSyntax current,
        int scopeId,
        int line,
        int column,
        List<SourceDiagnostic> diagnostics,
        ref ProcedureSyntax? procedure,
        ModuleSyntax module)
    {
        if (text is "]" or "]/")
        {
            current.EmitsRts = text == "]";
            module.Procedures.Add(current);
            procedure = null;
            return;
        }

        if (text.StartsWith("local ", StringComparison.Ordinal))
        {
            ParseLocals(text[6..], current, line, column + 6, diagnostics);
            return;
        }

        var algebra = Algebra.Match(text);
        if (algebra.Success)
        {
            ValidateRegisterWidths(
                algebra.Groups[1].Value,
                algebra.Groups["size"].Success ? algebra.Groups["size"].Value[0] : GetLocalSize(algebra.Groups[1].Value, current),
                algebra.Groups[3].Value,
                line,
                column,
                diagnostics);
            current.Statements.Add(new AlgebraStatement(
                algebra.Groups[1].Value,
                algebra.Groups[2].Value,
                algebra.Groups[3].Value.Trim(),
                line,
                column,
                scopeId));
            return;
        }

        if (DataRegisterAtStart.IsMatch(text))
        {
            diagnostics.Add(new SourceDiagnostic(line, column, "代数記法のデータレジスタには .b、.w、.l の幅指定が必要です"));
            return;
        }

        current.Statements.Add(new RawStatement(text, line, column, scopeId));
    }

    private static char? GetLocalSize(string name, ProcedureSyntax procedure)
    {
        var local = procedure.Locals.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return local is null ? null : TypeToSize(local.Type);
    }

    private static char TypeToSize(string type) => type switch
    {
        "byte" => 'b',
        "word" => 'w',
        "long" or "ptr" => 'l',
        _ => throw new InvalidOperationException($"未知の型: {type}"),
    };

    private static void ValidateRegisterWidths(
        string destination,
        char? expectedSize,
        string expression,
        int line,
        int column,
        List<SourceDiagnostic> diagnostics)
    {
        char? expressionSize = null;
        foreach (Match match in DataRegister.Matches(expression))
        {
            if (!match.Groups["size"].Success)
            {
                diagnostics.Add(new SourceDiagnostic(line, column, $"データレジスタ '{match.Value}' には .b、.w、.l の幅指定が必要です"));
                continue;
            }

            var size = char.ToLowerInvariant(match.Groups["size"].Value[0]);
            if (expectedSize is not null && size != expectedSize)
            {
                diagnostics.Add(new SourceDiagnostic(line, column, $"'{destination}' の幅と '{match.Value}' の幅が一致しません"));
            }

            if (expressionSize is not null && size != expressionSize)
            {
                diagnostics.Add(new SourceDiagnostic(line, column, $"式中のデータレジスタの幅が一致しません: {expression}"));
            }

            expressionSize = size;
        }
    }

    private static void ParseLocals(
        string declarations,
        ProcedureSyntax current,
        int line,
        int column,
        List<SourceDiagnostic> diagnostics)
    {
        foreach (var part in declarations.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var declaration = Declaration.Match(part);
            if (!declaration.Success)
            {
                diagnostics.Add(new SourceDiagnostic(line, column, "local は '名前: byte|word|long|ptr' をカンマ区切りで指定します"));
                continue;
            }

            var name = declaration.Groups[1].Value;
            if (!Identifier.IsMatch(name) || current.Locals.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new SourceDiagnostic(line, column, $"ローカル変数 '{name}' はすでに定義されています"));
                continue;
            }

            current.Locals.Add(new LocalSyntax(name, declaration.Groups[2].Value, line, column));
        }
    }

    private static IEnumerable<StatementPart> SplitStatements(string line)
    {
        var builder = new StringBuilder();
        var inString = false;
        var inBitField = false;
        var escaped = false;
        var startColumn = 1;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            var next = index + 1 < line.Length ? line[index + 1] : '\0';

            if (!inString && current == '/' && next == '/')
            {
                break;
            }

            if (current == '"' && !escaped)
            {
                inString = !inString;
            }

            if (!inString && current == ';')
            {
                yield return new StatementPart(builder.ToString(), startColumn);
                builder.Clear();
                startColumn = index + 2;
                escaped = false;
                continue;
            }

            if (!inString && current == '{'
                && BitFieldInstructionAtStart.IsMatch(builder.ToString().TrimStart()))
            {
                builder.Append(current);
                inBitField = true;
                escaped = false;
                continue;
            }

            if (!inString && current == '}' && inBitField)
            {
                builder.Append(current);
                inBitField = false;
                escaped = false;
                continue;
            }

            if (!inString && current == '{')
            {
                var prefix = builder.ToString();
                if (prefix.TrimStart().StartsWith("struct ", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(current);
                    yield return new StatementPart(builder.ToString(), startColumn);
                    builder.Clear();
                    startColumn = index + 2;
                }
                else
                {
                    if (builder.ToString().Trim().Length != 0)
                    {
                        yield return new StatementPart(builder.ToString(), startColumn);
                    }

                    builder.Clear();
                    yield return new StatementPart("{", index + 1);
                    startColumn = index + 2;
                }

                escaped = false;
                continue;
            }

            if (!inString && current == '}')
            {
                if (builder.ToString().Trim().Length != 0)
                {
                    yield return new StatementPart(builder.ToString(), startColumn);
                }

                builder.Clear();
                yield return new StatementPart("}", index + 1);
                startColumn = index + 2;
                escaped = false;
                continue;
            }

            builder.Append(current);
            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }
        }

        if (inString)
        {
            yield return new StatementPart(builder.ToString(), startColumn);
            yield break;
        }

        yield return new StatementPart(builder.ToString(), startColumn);
    }

    private sealed record StatementPart(string Text, int Column);

    private static int ParseAddressRegister(string text) =>
        text.Equals("sp", StringComparison.OrdinalIgnoreCase) ? 7 : text[1] - '0';
}
