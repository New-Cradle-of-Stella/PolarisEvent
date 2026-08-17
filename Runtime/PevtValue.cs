using System;
using System.Globalization;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 一个运行期 PEVT 值，只承载五种普通类型（8.2 节）；<c>handler</c> 是运行时专用包装，分开存放，不进入本类型。
    /// 只读结构体让赋值、传参和存入槽位天然是值复制，9.3 节要求的快照语义不需要额外的深拷贝步骤。
    /// </summary>
    public readonly struct PevtValue : IEquatable<PevtValue>
    {
        private readonly int _int;
        private readonly float _float;
        private readonly bool _bool;
        private readonly char _char;
        private readonly string _string;

        public PevtType Type { get; }

        private PevtValue(PevtType type, int i = 0, float f = 0f, bool b = false, char c = '\0', string s = null)
        {
            Type = type;
            _int = i;
            _float = f;
            _bool = b;
            _char = c;
            _string = s;
        }

        /// <summary>类型为 <see cref="PevtType.Error"/> 的哨兵值，只用来表示"槽位还没有值"。</summary>
        public static PevtValue None => default;

        public bool HasValue => Type.IsOrdinaryType();

        public static PevtValue FromInt(int value) => new PevtValue(PevtType.Int, i: value);

        public static PevtValue FromFloat(float value) => new PevtValue(PevtType.Float, f: value);

        public static PevtValue FromBool(bool value) => new PevtValue(PevtType.Bool, b: value);

        public static PevtValue FromChar(char value) => new PevtValue(PevtType.Char, c: value);

        /// <summary>PEVT 的 <c>string</c> 不接受 null；调用方传 null 时由上层转成 PEVTR3003。</summary>
        public static PevtValue FromString(string value) =>
            new PevtValue(PevtType.String, s: value ?? throw new ArgumentNullException(nameof(value)));

        public int AsInt => Type == PevtType.Int ? _int : throw WrongType(PevtType.Int);

        public float AsFloat => Type == PevtType.Float ? _float : throw WrongType(PevtType.Float);

        public bool AsBool => Type == PevtType.Bool ? _bool : throw WrongType(PevtType.Bool);

        public char AsChar => Type == PevtType.Char ? _char : throw WrongType(PevtType.Char);

        public string AsString => Type == PevtType.String ? _string : throw WrongType(PevtType.String);

        private InvalidOperationException WrongType(PevtType expected) =>
            new InvalidOperationException($"值的类型是 {Type.DisplayName()}，不能按 {expected.DisplayName()} 读取。");

        public bool Equals(PevtValue other)
        {
            if (Type != other.Type)
                return false;

            switch (Type)
            {
                case PevtType.Int: return _int == other._int;
                case PevtType.Float: return _float.Equals(other._float);
                case PevtType.Bool: return _bool == other._bool;
                case PevtType.Char: return _char == other._char;
                case PevtType.String: return string.Equals(_string, other._string, StringComparison.Ordinal);
                default: return true;
            }
        }

        public override bool Equals(object obj) => obj is PevtValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Type;
                switch (Type)
                {
                    case PevtType.Int: return (hash * 397) ^ _int;
                    case PevtType.Float: return (hash * 397) ^ _float.GetHashCode();
                    case PevtType.Bool: return (hash * 397) ^ (_bool ? 1 : 0);
                    case PevtType.Char: return (hash * 397) ^ _char;
                    case PevtType.String: return (hash * 397) ^ (_string?.GetHashCode() ?? 0);
                    default: return hash;
                }
            }
        }

        public static bool operator ==(PevtValue left, PevtValue right) => left.Equals(right);

        public static bool operator !=(PevtValue left, PevtValue right) => !left.Equals(right);

        public override string ToString()
        {
            switch (Type)
            {
                case PevtType.Int: return _int.ToString(CultureInfo.InvariantCulture);
                case PevtType.Float: return _float.ToString("R", CultureInfo.InvariantCulture);
                case PevtType.Bool: return _bool ? "true" : "false";
                case PevtType.Char: return _char.ToString();
                case PevtType.String: return _string;
                default: return "<none>";
            }
        }
    }
}
