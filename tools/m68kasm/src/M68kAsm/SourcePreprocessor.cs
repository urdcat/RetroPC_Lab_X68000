using System.Text;
using System.Text.RegularExpressions;

namespace M68kAsm;

internal sealed record PreprocessResult(
    string Source,
    IReadOnlyDictionary<string, long> Constants,
    IReadOnlyList<SourceDiagnostic> Diagnostics);

internal sealed class SourcePreprocessor
{
    private const int MaximumMacroExpansionPasses = 64;

    private static readonly Regex IncludeDirective = new(
        "^include\\s+\"(?<path>[^\"]+)\"$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MacroHeader = new(
        "^defmacro\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\\s*\\((?<parameters>[^)]*)\\))?\\s*=\\s*\\{",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConstantDeclaration = new(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+equ\\s+(?<expression>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProcedureStart = new(
        "^\\[[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled);

    public PreprocessResult Process(string source, string? sourcePath = null)
    {
        var diagnostics = new List<SourceDiagnostic>();
        var includeStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? fullSourcePath = null;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            fullSourcePath = Path.GetFullPath(sourcePath);
            includeStack.Add(fullSourcePath);
        }

        var withIncludes = ExpandIncludes(source, fullSourcePath, includeStack, diagnostics);
        var macros = new Dictionary<string, MacroDefinition>(StringComparer.OrdinalIgnoreCase);
        var withoutDefinitions = ExtractMacros(withIncludes, macros, diagnostics);
        var expanded = ExpandMacros(withoutDefinitions, macros, diagnostics);
        var constantDefinitions = new Dictionary<string, ConstantDefinition>(StringComparer.OrdinalIgnoreCase);
        var withoutConstants = ExtractConstants(expanded, constantDefinitions, diagnostics);
        var constants = ResolveConstants(constantDefinitions, diagnostics);
        var conditional = ExpandConditionalAssembly(withoutConstants, constants, diagnostics);
        return new PreprocessResult(conditional, constants, diagnostics);
    }

    private static string ExpandIncludes(
        string source,
        string? currentPath,
        HashSet<string> includeStack,
        List<SourceDiagnostic> diagnostics)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new StringBuilder();
        var baseDirectory = currentPath is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(currentPath) ?? Directory.GetCurrentDirectory();

        for (var index = 0; index < lines.Length; index++)
        {
            var candidate = StripLineComment(lines[index]).Trim();
            var include = IncludeDirective.Match(candidate);
            if (!include.Success)
            {
                output.Append(lines[index]);
                if (index + 1 < lines.Length)
                {
                    output.Append('\n');
                }

                continue;
            }

            var requestedPath = include.Groups["path"].Value;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(baseDirectory, requestedPath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                diagnostics.Add(new SourceDiagnostic(index + 1, 1, $"includeパスを解釈できません: {requestedPath}: {exception.Message}"));
                continue;
            }

            if (!includeStack.Add(fullPath))
            {
                diagnostics.Add(new SourceDiagnostic(index + 1, 1, $"includeが循環しています: {fullPath}"));
                continue;
            }

            try
            {
                var included = File.ReadAllText(fullPath);
                output.Append(ExpandIncludes(included, fullPath, includeStack, diagnostics));
                if (index + 1 < lines.Length)
                {
                    output.Append('\n');
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new SourceDiagnostic(index + 1, 1, $"includeファイルを読めません: {fullPath}: {exception.Message}"));
            }
            finally
            {
                includeStack.Remove(fullPath);
            }
        }

        return output.ToString();
    }

    private static string ExtractMacros(
        string source,
        Dictionary<string, MacroDefinition> macros,
        List<SourceDiagnostic> diagnostics)
    {
        var masked = source.ToCharArray();
        var searchStart = 0;
        while (TryFindKeyword(source, "defmacro", searchStart, out var definitionStart))
        {
            var header = MacroHeader.Match(source[definitionStart..]);
            if (!header.Success)
            {
                diagnostics.Add(new SourceDiagnostic(LineOf(source, definitionStart), 1, "defmacroは 'defmacro NAME[(ARGS)] = { ... }' で記述します"));
                searchStart = definitionStart + "defmacro".Length;
                continue;
            }

            var openingBrace = definitionStart + header.Length - 1;
            if (!TryFindMatchingBrace(source, openingBrace, out var closingBrace))
            {
                diagnostics.Add(new SourceDiagnostic(LineOf(source, definitionStart), 1, $"マクロ '{header.Groups["name"].Value}' が '}}' で閉じられていません"));
                break;
            }

            var name = header.Groups["name"].Value;
            var parameters = ParseParameters(header.Groups["parameters"].Value, name, LineOf(source, definitionStart), diagnostics);
            if (parameters is not null)
            {
                var body = source[(openingBrace + 1)..closingBrace];
                if (!macros.TryAdd(name, new MacroDefinition(name, parameters, body)))
                {
                    diagnostics.Add(new SourceDiagnostic(LineOf(source, definitionStart), 1, $"マクロ '{name}' はすでに定義されています"));
                }
            }

            for (var index = definitionStart; index <= closingBrace; index++)
            {
                if (masked[index] != '\n')
                {
                    masked[index] = ' ';
                }
            }

            searchStart = closingBrace + 1;
        }

        return new string(masked);
    }

    private static IReadOnlyList<string>? ParseParameters(
        string text,
        string macroName,
        int line,
        List<SourceDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parameters = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsIdentifier(item))
            {
                diagnostics.Add(new SourceDiagnostic(line, 1, $"マクロ '{macroName}' の引数名が不正です: {item}"));
                return null;
            }

            if (!seen.Add(item))
            {
                diagnostics.Add(new SourceDiagnostic(line, 1, $"マクロ '{macroName}' の引数 '{item}' が重複しています"));
                return null;
            }

            parameters.Add(item);
        }

        return parameters;
    }

    private static string ExpandMacros(
        string source,
        IReadOnlyDictionary<string, MacroDefinition> macros,
        List<SourceDiagnostic> diagnostics)
    {
        if (macros.Count == 0)
        {
            return source;
        }

        var current = source;
        for (var pass = 0; pass < MaximumMacroExpansionPasses; pass++)
        {
            var changed = false;
            current = TransformLogicalStatements(current, statement =>
            {
                if (!TryExpandInvocation(statement, macros, diagnostics, out var expansion))
                {
                    return statement;
                }

                changed = true;
                return expansion;
            });

            if (!changed)
            {
                return current;
            }
        }

        diagnostics.Add(new SourceDiagnostic(1, 1, $"マクロ展開が{MaximumMacroExpansionPasses}段を超えました。再帰呼び出しの可能性があります"));
        return current;
    }

    private static string ExpandConditionalAssembly(
        string source,
        IReadOnlyDictionary<string, long> constants,
        List<SourceDiagnostic> diagnostics)
    {
        var current = source;
        var searchStart = 0;
        while (TryFindKeyword(current, "ifasm", searchStart, out var start))
        {
            var cursor = start + "ifasm".Length;
            SkipWhiteSpace(current, ref cursor);
            if (cursor >= current.Length
                || current[cursor] != '('
                || !TryFindMatchingParenthesis(current, cursor, out var expressionEnd))
            {
                diagnostics.Add(new SourceDiagnostic(LineOf(current, start), 1, "条件アセンブルは 'ifasm (式) { ... }' で記述します"));
                searchStart = start + "ifasm".Length;
                continue;
            }

            var expression = current[(cursor + 1)..expressionEnd];
            cursor = expressionEnd + 1;
            SkipWhiteSpace(current, ref cursor);
            if (cursor >= current.Length
                || current[cursor] != '{'
                || !TryFindMatchingBrace(current, cursor, out var trueEnd))
            {
                diagnostics.Add(new SourceDiagnostic(LineOf(current, start), 1, "ifasmの本体を'{ ... }'で指定してください"));
                searchStart = expressionEnd + 1;
                continue;
            }

            var trueBody = current[(cursor + 1)..trueEnd];
            var replacementEnd = trueEnd + 1;
            var falseBody = string.Empty;
            cursor = replacementEnd;
            SkipWhiteSpace(current, ref cursor);
            if (current.AsSpan(cursor).StartsWith("else", StringComparison.OrdinalIgnoreCase)
                && (cursor + 4 == current.Length
                    || (current[cursor + 4] != '_' && !char.IsLetterOrDigit(current[cursor + 4]))))
            {
                cursor += 4;
                SkipWhiteSpace(current, ref cursor);
                if (cursor >= current.Length
                    || current[cursor] != '{'
                    || !TryFindMatchingBrace(current, cursor, out var falseEnd))
                {
                    diagnostics.Add(new SourceDiagnostic(LineOf(current, start), 1, "elseの本体を'{ ... }'で指定してください"));
                    searchStart = replacementEnd;
                    continue;
                }

                falseBody = current[(cursor + 1)..falseEnd];
                replacementEnd = falseEnd + 1;
            }

            if (!ExpressionEvaluator.TryEvaluate(
                    expression,
                    name => constants.TryGetValue(name, out var value)
                        ? SymbolResolution.Resolved(value)
                        : SymbolResolution.Failed($"ifasmでは定数ラベルだけを利用できます: {name}"),
                    out var condition,
                    out var error))
            {
                diagnostics.Add(new SourceDiagnostic(LineOf(current, start), 1, error));
                searchStart = replacementEnd;
                continue;
            }

            var selected = condition != 0 ? trueBody : falseBody;
            current = current[..start] + selected + current[replacementEnd..];
            searchStart = Math.Max(0, start - 1);
        }

        return current;
    }

    private static string ExtractConstants(
        string source,
        Dictionary<string, ConstantDefinition> definitions,
        List<SourceDiagnostic> diagnostics)
    {
        var inProcedure = false;
        var topLevelBraceDepth = 0;
        return TransformLogicalStatements(source, (statement, line) =>
        {
            var code = StripLineComment(statement).Trim();
            var declaration = ConstantDeclaration.Match(code);
            if (declaration.Success)
            {
                var column = statement.Length - statement.TrimStart().Length + 1;
                var name = declaration.Groups["name"].Value;
                if (inProcedure || topLevelBraceDepth != 0)
                {
                    diagnostics.Add(new SourceDiagnostic(line, column, $"定数ラベル '{name}' はトップレベルで定義してください"));
                }
                else if (!definitions.TryAdd(
                             name,
                             new ConstantDefinition(
                                 name,
                                 declaration.Groups["expression"].Value.Trim(),
                                 line,
                                 column)))
                {
                    diagnostics.Add(new SourceDiagnostic(line, column, $"定数ラベル '{name}' はすでに定義されています"));
                }

                return MaskStatement(statement);
            }

            if (!inProcedure && ProcedureStart.IsMatch(code))
            {
                inProcedure = true;
                return statement;
            }

            if (inProcedure)
            {
                if (code is "]" or "]/")
                {
                    inProcedure = false;
                }

                return statement;
            }

            topLevelBraceDepth = Math.Max(0, topLevelBraceDepth + BraceBalance(code));
            return statement;
        });
    }

    private static IReadOnlyDictionary<string, long> ResolveConstants(
        IReadOnlyDictionary<string, ConstantDefinition> definitions,
        List<SourceDiagnostic> diagnostics)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, ConstantResolutionState>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        SymbolResolution Resolve(string name)
        {
            if (values.TryGetValue(name, out var resolvedValue))
            {
                return SymbolResolution.Resolved(resolvedValue);
            }

            if (!definitions.TryGetValue(name, out var definition))
            {
                return SymbolResolution.Failed($"未定義の定数ラベルです: {name}");
            }

            if (states.TryGetValue(name, out var state))
            {
                if (state == ConstantResolutionState.Resolving)
                {
                    var cycleStart = stack.FindIndex(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
                    var cycle = stack.Skip(Math.Max(0, cycleStart)).Append(name);
                    return SymbolResolution.Failed($"定数ラベル参照が循環しています: {string.Join(" -> ", cycle)}");
                }

                if (state == ConstantResolutionState.Failed)
                {
                    return SymbolResolution.Failed(errors[name]);
                }
            }

            states[name] = ConstantResolutionState.Resolving;
            stack.Add(name);
            var success = ExpressionEvaluator.TryEvaluate(
                definition.Expression,
                Resolve,
                out var value,
                out var error);
            stack.RemoveAt(stack.Count - 1);

            if (!success)
            {
                states[name] = ConstantResolutionState.Failed;
                errors[name] = error;
                return SymbolResolution.Failed(error);
            }

            states[name] = ConstantResolutionState.Resolved;
            values[name] = value;
            return SymbolResolution.Resolved(value);
        }

        foreach (var definition in definitions.Values)
        {
            var resolution = Resolve(definition.Name);
            if (!resolution.Success)
            {
                diagnostics.Add(new SourceDiagnostic(definition.Line, definition.Column, resolution.Error));
            }
        }

        return values;
    }

    private static string MaskStatement(string statement) =>
        new(statement.Select(value => value is '\r' or '\n' ? value : ' ').ToArray());

    private static int BraceBalance(string text)
    {
        var balance = 0;
        var inString = false;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (!inString && current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                break;
            }

            if (current == '"' && !escaped)
            {
                inString = !inString;
            }
            else if (!inString)
            {
                balance += current switch
                {
                    '{' => 1,
                    '}' => -1,
                    _ => 0,
                };
            }

            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }
        }

        return balance;
    }

    private static string TransformLogicalStatements(string source, Func<string, string> transform)
        => TransformLogicalStatements(source, (statement, _) => transform(statement));

    private static string TransformLogicalStatements(string source, Func<string, int, string> transform)
    {
        var output = new StringBuilder();
        var statement = new StringBuilder();
        var parenthesisDepth = 0;
        var inString = false;
        var escaped = false;
        var lineComment = false;
        var currentLine = 1;
        var statementLine = 1;

        foreach (var current in source)
        {
            if (lineComment)
            {
                statement.Append(current);
                if (current == '\n')
                {
                    output.Append(transform(statement.ToString(), statementLine));
                    statement.Clear();
                    lineComment = false;
                    currentLine++;
                    statementLine = currentLine;
                }

                continue;
            }

            if (!inString && current == '/' && statement.Length > 0 && statement[^1] == '/')
            {
                lineComment = true;
                statement.Append(current);
                continue;
            }

            if (current == '"' && !escaped)
            {
                inString = !inString;
            }

            if (!inString)
            {
                if (current == '(')
                {
                    parenthesisDepth++;
                }
                else if (current == ')')
                {
                    parenthesisDepth--;
                }

                if ((current == '\n' || current == ';') && parenthesisDepth == 0)
                {
                    output.Append(transform(statement.ToString(), statementLine));
                    output.Append(current);
                    statement.Clear();
                    escaped = false;
                    if (current == '\n')
                    {
                        currentLine++;
                    }

                    statementLine = currentLine;
                    continue;
                }
            }

            statement.Append(current);
            if (current == '\n')
            {
                currentLine++;
            }

            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }
        }

        if (statement.Length != 0)
        {
            output.Append(transform(statement.ToString(), statementLine));
        }

        return output.ToString();
    }

    private static bool TryExpandInvocation(
        string statement,
        IReadOnlyDictionary<string, MacroDefinition> macros,
        List<SourceDiagnostic> diagnostics,
        out string expansion)
    {
        expansion = string.Empty;
        var code = StripLineComment(statement).Trim();
        if (code.Length == 0)
        {
            return false;
        }

        var identifierLength = 0;
        while (identifierLength < code.Length && (identifierLength == 0
                   ? code[identifierLength] == '_' || char.IsLetter(code[identifierLength])
                   : code[identifierLength] == '_' || char.IsLetterOrDigit(code[identifierLength])))
        {
            identifierLength++;
        }

        if (identifierLength == 0)
        {
            return false;
        }

        var name = code[..identifierLength];
        if (!macros.TryGetValue(name, out var macro))
        {
            return false;
        }

        var remainder = code[identifierLength..].Trim();
        IReadOnlyList<string> arguments;
        if (remainder.Length == 0)
        {
            arguments = [];
        }
        else
        {
            if (remainder[0] != '(' || !TryFindMatchingParenthesis(remainder, 0, out var closing) || closing != remainder.Length - 1)
            {
                diagnostics.Add(new SourceDiagnostic(1, 1, $"マクロ '{name}' の呼び出しを解釈できません: {code}"));
                return false;
            }

            arguments = SplitArguments(remainder[1..^1]);
        }

        if (arguments.Count != macro.Parameters.Count)
        {
            diagnostics.Add(new SourceDiagnostic(1, 1, $"マクロ '{name}' の引数は{macro.Parameters.Count}個必要ですが、{arguments.Count}個指定されています"));
            return false;
        }

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index++)
        {
            replacements.Add(macro.Parameters[index], arguments[index]);
        }

        var indentationLength = statement.Length - statement.TrimStart().Length;
        var indentation = statement[..indentationLength];
        var body = SubstituteIdentifiers(macro.Body, replacements).Trim('\r', '\n');
        expansion = $"{indentation}{{\n{body}\n{indentation}}}";
        return true;
    }

    private static List<string> SplitArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<string>();
        var builder = new StringBuilder();
        var parenthesisDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var inString = false;
        var escaped = false;
        foreach (var current in text)
        {
            if (current == '"' && !escaped)
            {
                inString = !inString;
            }

            if (!inString)
            {
                switch (current)
                {
                    case '(':
                        parenthesisDepth++;
                        break;
                    case ')':
                        parenthesisDepth--;
                        break;
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        braceDepth--;
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        bracketDepth--;
                        break;
                    case ',' when parenthesisDepth == 0 && braceDepth == 0 && bracketDepth == 0:
                        result.Add(builder.ToString().Trim());
                        builder.Clear();
                        continue;
                }
            }

            builder.Append(current);
            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }
        }

        result.Add(builder.ToString().Trim());
        return result;
    }

    private static string SubstituteIdentifiers(string body, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return body;
        }

        var output = new StringBuilder();
        var inString = false;
        var escaped = false;
        var lineComment = false;
        for (var index = 0; index < body.Length;)
        {
            var current = body[index];
            if (lineComment)
            {
                output.Append(current);
                index++;
                if (current == '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (!inString && current == '/' && index + 1 < body.Length && body[index + 1] == '/')
            {
                output.Append("//");
                index += 2;
                lineComment = true;
                continue;
            }

            if (current == '"' && !escaped)
            {
                inString = !inString;
                output.Append(current);
                index++;
                continue;
            }

            if (!inString && (current == '_' || char.IsLetter(current)))
            {
                var start = index++;
                while (index < body.Length && (body[index] == '_' || char.IsLetterOrDigit(body[index])))
                {
                    index++;
                }

                var identifier = body[start..index];
                output.Append(replacements.TryGetValue(identifier, out var replacement) ? replacement : identifier);
                continue;
            }

            output.Append(current);
            index++;
            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }
        }

        return output.ToString();
    }

    private static bool TryFindKeyword(string source, string keyword, int start, out int position)
    {
        var inString = false;
        var escaped = false;
        var lineComment = false;
        for (var index = start; index <= source.Length - keyword.Length; index++)
        {
            var current = source[index];
            if (lineComment)
            {
                if (current == '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (!inString && current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (current == '"' && !escaped)
            {
                inString = !inString;
                continue;
            }

            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }

            if (inString
                || !source.AsSpan(index).StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                || (index > 0 && (source[index - 1] == '_' || char.IsLetterOrDigit(source[index - 1])))
                || (index + keyword.Length < source.Length
                    && (source[index + keyword.Length] == '_' || char.IsLetterOrDigit(source[index + keyword.Length]))))
            {
                continue;
            }

            position = index;
            return true;
        }

        position = -1;
        return false;
    }

    private static bool TryFindMatchingBrace(string source, int opening, out int closing) =>
        TryFindMatchingDelimiter(source, opening, '{', '}', out closing);

    private static bool TryFindMatchingParenthesis(string source, int opening, out int closing) =>
        TryFindMatchingDelimiter(source, opening, '(', ')', out closing);

    private static bool TryFindMatchingDelimiter(string source, int opening, char open, char close, out int closing)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var lineComment = false;
        for (var index = opening; index < source.Length; index++)
        {
            var current = source[index];
            if (lineComment)
            {
                if (current == '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (!inString && current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (current == '"' && !escaped)
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (current == open)
                {
                    depth++;
                }
                else if (current == close && --depth == 0)
                {
                    closing = index;
                    return true;
                }
            }

            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }
        }

        closing = -1;
        return false;
    }

    private static string StripLineComment(string text)
    {
        var inString = false;
        var escaped = false;
        for (var index = 0; index + 1 < text.Length; index++)
        {
            var current = text[index];
            if (current == '"' && !escaped)
            {
                inString = !inString;
            }

            if (!inString && current == '/' && text[index + 1] == '/')
            {
                return text[..index];
            }

            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }
        }

        return text;
    }

    private static int LineOf(string source, int position)
    {
        var line = 1;
        for (var index = 0; index < position; index++)
        {
            if (source[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static void SkipWhiteSpace(string source, ref int position)
    {
        while (position < source.Length && char.IsWhiteSpace(source[position]))
        {
            position++;
        }
    }

    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0 || (text[0] != '_' && !char.IsLetter(text[0])))
        {
            return false;
        }

        return text.Skip(1).All(value => value == '_' || char.IsLetterOrDigit(value));
    }

    private sealed record MacroDefinition(
        string Name,
        IReadOnlyList<string> Parameters,
        string Body);

    private sealed record ConstantDefinition(
        string Name,
        string Expression,
        int Line,
        int Column);

    private enum ConstantResolutionState
    {
        Resolving,
        Resolved,
        Failed,
    }
}
