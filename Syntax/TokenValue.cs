using System;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 一个 token 携带的已解码字面量值，覆盖 8.9 节整数/浮点数/字符串/字符/布尔五种字面量。
    /// 非字面量 token（关键字、标点等）使用 <see cref="None"/>。
    /// </summary>
    public readonly struct TokenValue : IEquatable<TokenValue>
    {
        public TokenValueKind Kind { get; }

        private readonly int _intValue;
        private readonly float _floatValue;
        private readonly bool _boolValue;
        private readonly char _charValue;
        private readonly string _stringValue;

        public static readonly TokenValue None = default;

        private TokenValue(TokenValueKind kind, int intValue = 0, float floatValue = 0f,
            bool boolValue = false, char charValue = '\0', string stringValue = null)
        {
            Kind = kind;
            _intValue = intValue;
            _floatValue = floatValue;
            _boolValue = boolValue;
            _charValue = charValue;
            _stringValue = stringValue;
        }

        public static TokenValue FromInteger(int value) => new TokenValue(TokenValueKind.Integer, intValue: value);
        public static TokenValue FromFloat(float value) => new TokenValue(TokenValueKind.Float, floatValue: value);
        public static TokenValue FromBoolean(bool value) => new TokenValue(TokenValueKind.Boolean, boolValue: value);
        public static TokenValue FromChar(char value) => new TokenValue(TokenValueKind.Char, charValue: value);

        public static TokenValue FromString(string value) =>
            new TokenValue(TokenValueKind.String, stringValue: value ?? throw new ArgumentNullException(nameof(value)));

        private InvalidOperationException WrongKind(TokenValueKind expected) =>
            new InvalidOperationException($"TokenValue 持有 {Kind}，不是 {expected}。");

        public int AsInteger => Kind == TokenValueKind.Integer ? _intValue : throw WrongKind(TokenValueKind.Integer);
        public float AsFloat => Kind == TokenValueKind.Float ? _floatValue : throw WrongKind(TokenValueKind.Float);
        public bool AsBoolean => Kind == TokenValueKind.Boolean ? _boolValue : throw WrongKind(TokenValueKind.Boolean);
        public char AsChar => Kind == TokenValueKind.Char ? _charValue : throw WrongKind(TokenValueKind.Char);
        public string AsString => Kind == TokenValueKind.String ? _stringValue : throw WrongKind(TokenValueKind.String);

        public bool Equals(TokenValue other) => Kind == other.Kind && Kind switch
        {
            TokenValueKind.None => true,
            TokenValueKind.Integer => _intValue == other._intValue,
            TokenValueKind.Float => _floatValue.Equals(other._floatValue),
            TokenValueKind.Boolean => _boolValue == other._boolValue,
            TokenValueKind.Char => _charValue == other._charValue,
            TokenValueKind.String => _stringValue == other._stringValue,
            _ => false,
        };

        public override bool Equals(object obj) => obj is TokenValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ _intValue;
                hash = (hash * 397) ^ _floatValue.GetHashCode();
                hash = (hash * 397) ^ _boolValue.GetHashCode();
                hash = (hash * 397) ^ _charValue.GetHashCode();
                return (hash * 397) ^ (_stringValue?.GetHashCode() ?? 0);
            }
        }

        public override string ToString() => Kind switch
        {
            TokenValueKind.Integer => _intValue.ToString(),
            TokenValueKind.Float => _floatValue.ToString(),
            TokenValueKind.Boolean => _boolValue ? "true" : "false",
            TokenValueKind.Char => _charValue.ToString(),
            TokenValueKind.String => _stringValue,
            _ => "<none>",
        };
    }
}
