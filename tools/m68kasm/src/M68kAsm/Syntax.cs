namespace M68kAsm;

public sealed record SourceDiagnostic(int Line, int Column, string Message)
{
    public override string ToString() => $"{Line}:{Column}: error: {Message}";
}

public sealed class ModuleSyntax
{
    public ModuleSyntax(IReadOnlyDictionary<string, long>? constants = null)
    {
        Constants = constants ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, long> Constants { get; }
    public List<StructureSyntax> Structures { get; } = new();
    public List<ProcedureSyntax> Procedures { get; } = new();
}

public sealed class StructureSyntax
{
    public StructureSyntax(string name, int line, int? baseRegister = null)
    {
        Name = name;
        Line = line;
        BaseRegister = baseRegister;
    }

    public string Name { get; }
    public int Line { get; }
    public int? BaseRegister { get; }
    public List<FieldSyntax> Fields { get; } = new();
}

public sealed record FieldSyntax(string Name, string Type, int Line, int Column);

public sealed class ProcedureSyntax
{
    public ProcedureSyntax(string name, int line)
    {
        Name = name;
        Line = line;
    }

    public string Name { get; }
    public int Line { get; }
    public bool EmitsRts { get; set; } = true;
    public List<LocalSyntax> Locals { get; } = new();
    public List<StatementSyntax> Statements { get; } = new();
    public Dictionary<int, int?> ScopeParents { get; } = new() { [0] = null };

    public int CreateScope(int parent)
    {
        var scope = ScopeParents.Count;
        ScopeParents.Add(scope, parent);
        return scope;
    }
}

public sealed record LocalSyntax(string Name, string Type, int Line, int Column);

public abstract record StatementSyntax(int Line, int Column, int ScopeId);

public sealed record AlgebraStatement(
    string Destination,
    string Operator,
    string Expression,
    int Line,
    int Column,
    int ScopeId = 0) : StatementSyntax(Line, Column, ScopeId);

public sealed record RawStatement(
    string Text,
    int Line,
    int Column,
    int ScopeId = 0) : StatementSyntax(Line, Column, ScopeId);

public sealed record ParseResult(ModuleSyntax Module, IReadOnlyList<SourceDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0;
}
