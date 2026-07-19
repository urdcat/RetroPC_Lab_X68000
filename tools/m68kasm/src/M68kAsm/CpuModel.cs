namespace M68kAsm;

public enum CpuModel
{
    Mc68000 = 0,
    Mc68010 = 1,
    Mc68020 = 2,
    Mc68030 = 3,
}

public static class CpuModelParser
{
    public static bool TryParse(string text, out CpuModel cpu)
    {
        cpu = text.Trim().ToLowerInvariant() switch
        {
            "68000" or "mc68000" => CpuModel.Mc68000,
            "68010" or "mc68010" => CpuModel.Mc68010,
            "68020" or "mc68020" => CpuModel.Mc68020,
            "68030" or "mc68030" => CpuModel.Mc68030,
            _ => (CpuModel)(-1),
        };
        return cpu >= CpuModel.Mc68000;
    }

    public static string ToCommandLineName(this CpuModel cpu) => cpu switch
    {
        CpuModel.Mc68000 => "68000",
        CpuModel.Mc68010 => "68010",
        CpuModel.Mc68020 => "68020",
        CpuModel.Mc68030 => "68030",
        _ => throw new ArgumentOutOfRangeException(nameof(cpu)),
    };
}
