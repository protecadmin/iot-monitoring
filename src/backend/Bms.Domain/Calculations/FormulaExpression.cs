using System.Globalization;

namespace Bms.Domain.Calculations;

/// <summary>
/// An arithmetic formula over a reading's parameters, such as
/// <c>voltage * current * powerFactor / 1000</c>: numbers, parameter keys,
/// + - * /, unary minus and parentheses — nothing else, so a user-entered
/// formula can never call into anything.
///
/// The same parsed formula is evaluated live (<see cref="Evaluate"/>) and
/// rendered into the history query (<see cref="Render"/>), so a value pushed
/// in real time and the same reading read back from history agree.
/// </summary>
public sealed class FormulaExpression
{
    public const int MaxLength = 256;
    private const int MaxDepth = 32;

    private readonly Node _root;

    private FormulaExpression(string text, Node root, IReadOnlyList<string> variables)
    {
        Text = text;
        _root = root;
        Variables = variables;
    }

    /// <summary>The formula as written, trimmed.</summary>
    public string Text { get; }

    /// <summary>Parameter keys the formula reads, each once, in order of first use.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <exception cref="FormatException">The formula is empty, too long or not valid arithmetic; the message says where.</exception>
    public static FormulaExpression Parse(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new FormatException("Formula is empty");
        if (trimmed.Length > MaxLength)
            throw new FormatException($"Formula can be at most {MaxLength} characters");

        var parser = new Parser(trimmed);
        var root = parser.ParseAll();
        if (parser.Variables.Count == 0)
            throw new FormatException("Formula must use at least one parameter");
        return new FormulaExpression(trimmed, root, parser.Variables);
    }

    public static bool TryParse(string? text, out FormulaExpression? formula, out string? error)
    {
        try
        {
            formula = Parse(text);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            formula = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The formula's value for one reading. Null when the reading lacks any
    /// parameter the formula uses, or the result isn't a finite number
    /// (division by zero) — a missing value, never a made-up one.
    /// </summary>
    public double? Evaluate(IReadOnlyDictionary<string, double> values)
    {
        var result = _root.Evaluate(values);
        return result is { } v && double.IsFinite(v) ? v : null;
    }

    /// <summary>
    /// The formula as fully parenthesised infix, with each parameter written by
    /// <paramref name="variable"/> — e.g. as a column reference in a query.
    /// Numbers always carry a decimal point and never an exponent, so the text
    /// is also a valid float expression in languages that won't mix integer
    /// and float literals (Flux).
    /// </summary>
    public string Render(Func<string, string> variable) => _root.Render(variable);

    public override string ToString() => Text;

    private abstract record Node
    {
        public abstract double? Evaluate(IReadOnlyDictionary<string, double> values);
        public abstract string Render(Func<string, string> variable);
    }

    private sealed record NumberNode(double Value) : Node
    {
        public override double? Evaluate(IReadOnlyDictionary<string, double> values) => Value;
        public override string Render(Func<string, string> variable) => Value.ToString("0.0###############", CultureInfo.InvariantCulture);
    }

    private sealed record VariableNode(string Key) : Node
    {
        public override double? Evaluate(IReadOnlyDictionary<string, double> values) => values.TryGetValue(Key, out var v) ? v : null;
        public override string Render(Func<string, string> variable) => variable(Key);
    }

    private sealed record NegateNode(Node Operand) : Node
    {
        public override double? Evaluate(IReadOnlyDictionary<string, double> values) => -Operand.Evaluate(values);
        public override string Render(Func<string, string> variable) => $"(-{Operand.Render(variable)})";
    }

    private sealed record BinaryNode(char Op, Node Left, Node Right) : Node
    {
        public override double? Evaluate(IReadOnlyDictionary<string, double> values)
        {
            if (Left.Evaluate(values) is not { } l || Right.Evaluate(values) is not { } r)
                return null;
            return Op switch
            {
                '+' => l + r,
                '-' => l - r,
                '*' => l * r,
                _ => l / r
            };
        }

        public override string Render(Func<string, string> variable) => $"({Left.Render(variable)} {Op} {Right.Render(variable)})";
    }

    /// <summary>
    /// Recursive descent over: expr := term (('+'|'-') term)*; term := unary
    /// (('*'|'/') unary)*; unary := '-' unary | primary; primary := number |
    /// key | '(' expr ')'.
    /// </summary>
    private sealed class Parser(string text)
    {
        private int _pos;
        private int _depth;
        private readonly List<string> _variables = [];

        public IReadOnlyList<string> Variables => _variables;

        public Node ParseAll()
        {
            var node = ParseExpression();
            SkipWhitespace();
            if (_pos < text.Length)
                throw Error($"Unexpected '{text[_pos]}'");
            return node;
        }

        private Node ParseExpression()
        {
            var node = ParseTerm();
            while (TryConsume('+', '-') is { } op)
                node = new BinaryNode(op, node, ParseTerm());
            return node;
        }

        private Node ParseTerm()
        {
            var node = ParseUnary();
            while (TryConsume('*', '/') is { } op)
                node = new BinaryNode(op, node, ParseUnary());
            return node;
        }

        private Node ParseUnary()
        {
            if (++_depth > MaxDepth)
                throw Error("Formula is nested too deeply");
            try
            {
                return TryConsume('-') is not null ? new NegateNode(ParseUnary()) : ParsePrimary();
            }
            finally
            {
                _depth--;
            }
        }

        private Node ParsePrimary()
        {
            SkipWhitespace();
            if (_pos >= text.Length)
                throw Error("Formula ends too early");

            var c = text[_pos];
            if (c == '(')
            {
                _pos++;
                var inner = ParseExpression();
                if (TryConsume(')') is null)
                    throw Error("Missing ')'");
                return inner;
            }

            if (char.IsAsciiDigit(c) || c == '.')
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.'))
                    _pos++;
                var literal = text[start.._pos];
                if (!double.TryParse(literal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    throw Error($"'{literal}' is not a number", start);
                return new NumberNode(number);
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsAsciiLetterOrDigit(text[_pos]) || text[_pos] == '_'))
                    _pos++;
                var key = text[start.._pos];
                if (!_variables.Contains(key, StringComparer.OrdinalIgnoreCase))
                    _variables.Add(key);
                return new VariableNode(key);
            }

            throw Error($"Unexpected '{c}'");
        }

        private char? TryConsume(params char[] options)
        {
            SkipWhitespace();
            if (_pos < text.Length && options.Contains(text[_pos]))
                return text[_pos++];
            return null;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
                _pos++;
        }

        private FormatException Error(string message, int? at = null) => new($"{message} at position {(at ?? _pos) + 1}");
    }
}
