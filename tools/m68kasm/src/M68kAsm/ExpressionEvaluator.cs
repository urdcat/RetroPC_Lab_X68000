using System.Globalization;

namespace M68kAsm;

internal readonly record struct SymbolResolution(bool Success, long Value, string Error)
{
    public static SymbolResolution Resolved(long value) => new(true, value, string.Empty);

    public static SymbolResolution Failed(string error) => new(false, 0, error);
}

internal static class ExpressionEvaluator
{
    public static bool IsValid(string expression)
    {
        var parser = new Parser(expression, _ => SymbolResolution.Resolved(0));
        return parser.TryParse(out _, out _);
    }

    public static bool TryEvaluate(
        string expression,
        Func<string, SymbolResolution> resolveSymbol,
        out long value,
        out string error)
    {
        var parser = new Parser(expression, resolveSymbol);
        return parser.TryParse(out value, out error);
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly Func<string, SymbolResolution> _resolveSymbol;
        private int _position;

        public Parser(string text, Func<string, SymbolResolution> resolveSymbol)
        {
            _text = text;
            _resolveSymbol = resolveSymbol;
        }

        public bool TryParse(out long value, out string error)
        {
            value = 0;
            error = string.Empty;
            try
            {
                SkipWhiteSpace();
                value = ParseLogicalOr();
                SkipWhiteSpace();
                if (_position != _text.Length)
                {
                    throw Error($"予期しない文字です: '{_text[_position]}'");
                }

                return true;
            }
            catch (ExpressionException exception)
            {
                error = exception.Message;
                return false;
            }
            catch (OverflowException)
            {
                error = $"式が64ビット範囲を超えています: {_text}";
                return false;
            }
        }

        private long ParseLogicalOr()
        {
            var value = ParseLogicalAnd();
            while (Match("||"))
            {
                var right = ParseLogicalAnd();
                value = value != 0 || right != 0 ? 1 : 0;
            }

            return value;
        }

        private long ParseLogicalAnd()
        {
            var value = ParseBitwiseOr();
            while (Match("&&"))
            {
                var right = ParseBitwiseOr();
                value = value != 0 && right != 0 ? 1 : 0;
            }

            return value;
        }

        private long ParseBitwiseOr()
        {
            var value = ParseBitwiseXor();
            while (MatchSingle('|', '|'))
            {
                value |= ParseBitwiseXor();
            }

            return value;
        }

        private long ParseBitwiseXor()
        {
            var value = ParseBitwiseAnd();
            while (Match("^"))
            {
                value ^= ParseBitwiseAnd();
            }

            return value;
        }

        private long ParseBitwiseAnd()
        {
            var value = ParseEquality();
            while (MatchSingle('&', '&'))
            {
                value &= ParseEquality();
            }

            return value;
        }

        private long ParseEquality()
        {
            var value = ParseRelational();
            while (true)
            {
                if (Match("=="))
                {
                    value = value == ParseRelational() ? 1 : 0;
                }
                else if (Match("!="))
                {
                    value = value != ParseRelational() ? 1 : 0;
                }
                else
                {
                    return value;
                }
            }
        }

        private long ParseRelational()
        {
            var value = ParseShift();
            while (true)
            {
                if (Match("<="))
                {
                    value = value <= ParseShift() ? 1 : 0;
                }
                else if (Match(">="))
                {
                    value = value >= ParseShift() ? 1 : 0;
                }
                else if (MatchSingle('<', '<'))
                {
                    value = value < ParseShift() ? 1 : 0;
                }
                else if (MatchSingle('>', '>'))
                {
                    value = value > ParseShift() ? 1 : 0;
                }
                else
                {
                    return value;
                }
            }
        }

        private long ParseShift()
        {
            var value = ParseAdditive();
            while (true)
            {
                if (Match("<<"))
                {
                    var count = ParseAdditive();
                    ValidateShift(count);
                    value = checked(value << (int)count);
                }
                else if (Match(">>"))
                {
                    var count = ParseAdditive();
                    ValidateShift(count);
                    value >>= (int)count;
                }
                else
                {
                    return value;
                }
            }
        }

        private long ParseAdditive()
        {
            var value = ParseMultiplicative();
            while (true)
            {
                if (Match("+"))
                {
                    value = checked(value + ParseMultiplicative());
                }
                else if (Match("-"))
                {
                    value = checked(value - ParseMultiplicative());
                }
                else
                {
                    return value;
                }
            }
        }

        private long ParseMultiplicative()
        {
            var value = ParseUnary();
            while (true)
            {
                if (Match("*"))
                {
                    value = checked(value * ParseUnary());
                }
                else if (Match("/"))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0)
                    {
                        throw Error("0で除算できません");
                    }

                    value = checked(value / divisor);
                }
                else if (Match("%"))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0)
                    {
                        throw Error("0で剰余を計算できません");
                    }

                    value = checked(value % divisor);
                }
                else
                {
                    return value;
                }
            }
        }

        private long ParseUnary()
        {
            if (Match("+"))
            {
                return ParseUnary();
            }

            if (Match("-"))
            {
                return checked(-ParseUnary());
            }

            if (Match("~"))
            {
                return ~ParseUnary();
            }

            if (Match("!"))
            {
                return ParseUnary() == 0 ? 1 : 0;
            }

            return ParsePrimary();
        }

        private long ParsePrimary()
        {
            SkipWhiteSpace();
            if (Match("("))
            {
                var value = ParseLogicalOr();
                if (!Match(")"))
                {
                    throw Error("閉じ括弧 ')' がありません");
                }

                return value;
            }

            if (_position >= _text.Length)
            {
                throw Error("式が途中で終わっています");
            }

            if (IsIdentifierStart(_text[_position]))
            {
                var name = ReadIdentifier();
                var resolution = _resolveSymbol(name);
                if (!resolution.Success)
                {
                    throw Error(resolution.Error);
                }

                return resolution.Value;
            }

            return ReadNumber();
        }

        private long ReadNumber()
        {
            SkipWhiteSpace();
            var start = _position;
            var radix = 10;
            if (Peek('$'))
            {
                radix = 16;
                _position++;
            }
            else if (Peek('%'))
            {
                radix = 2;
                _position++;
            }
            else if (_position + 1 < _text.Length
                     && _text[_position] == '0'
                     && (_text[_position + 1] is 'x' or 'X'))
            {
                radix = 16;
                _position += 2;
            }

            var digitsStart = _position;
            while (_position < _text.Length && IsDigitForRadix(_text[_position], radix))
            {
                _position++;
            }

            if (_position == digitsStart)
            {
                _position = start;
                throw Error("数値またはシンボルが必要です");
            }

            var digits = _text[digitsStart.._position];
            try
            {
                return radix switch
                {
                    10 => long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
                    16 => long.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                    2 => Convert.ToInt64(digits, 2),
                    _ => throw new InvalidOperationException(),
                };
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
            {
                throw Error($"数値を解釈できません: {_text[start.._position]}");
            }
        }

        private string ReadIdentifier()
        {
            var start = _position++;
            while (_position < _text.Length && IsIdentifierPart(_text[_position]))
            {
                _position++;
            }

            return _text[start.._position];
        }

        private bool Match(string token)
        {
            SkipWhiteSpace();
            if (!_text.AsSpan(_position).StartsWith(token, StringComparison.Ordinal))
            {
                return false;
            }

            _position += token.Length;
            return true;
        }

        private bool MatchSingle(char token, char excludedFollower)
        {
            SkipWhiteSpace();
            if (!Peek(token)
                || (_position + 1 < _text.Length && _text[_position + 1] == excludedFollower))
            {
                return false;
            }

            _position++;
            return true;
        }

        private bool Peek(char value) => _position < _text.Length && _text[_position] == value;

        private void SkipWhiteSpace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        private ExpressionException Error(string message) =>
            new($"{message}（位置{_position + 1}: {_text}）");

        private static void ValidateShift(long count)
        {
            if (count is < 0 or > 63)
            {
                throw new ExpressionException($"シフト量は0から63で指定します: {count}");
            }
        }

        private static bool IsIdentifierStart(char value) =>
            value == '.' || value == '_' || char.IsLetter(value);

        private static bool IsIdentifierPart(char value) =>
            value == '.' || value == '_' || char.IsLetterOrDigit(value);

        private static bool IsDigitForRadix(char value, int radix) => radix switch
        {
            2 => value is '0' or '1',
            10 => value is >= '0' and <= '9',
            16 => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F',
            _ => false,
        };
    }

    private sealed class ExpressionException(string message) : Exception(message);
}
