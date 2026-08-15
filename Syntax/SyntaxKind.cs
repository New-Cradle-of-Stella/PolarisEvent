namespace Polaris.Pevt.Syntax
{
    /// <summary>词法 token 种类（19 节终结符 + 9.6 节保留关键字）。缺失 token 复用期望的 Kind，见 <see cref="SyntaxToken.IsMissing"/>。</summary>
    public enum SyntaxKind
    {
        None, EndOfFileToken, BadToken,

        IdentifierToken, EventIdLiteralToken, IntegerLiteralToken, FloatLiteralToken,
        StringLiteralToken, CharLiteralToken, RawContentToken,

        IdKeyword, EnableKeyword, CsKeyword, CmdKeyword, EndKeyword, BlockKeyword, CallEvtKeyword,
        IfKeyword, ElifKeyword, ElseKeyword, EndIfKeyword,
        WhileKeyword, EndWhileKeyword,
        SwitchKeyword, CaseKeyword, DefaultKeyword, EndSwitchKeyword,
        GotoKeyword, VarKeyword, ConstKeyword,
        IntKeyword, FloatKeyword, BoolKeyword, CharKeyword, StringKeyword,
        TrueKeyword, FalseKeyword, ReturnKeyword, EndBlockKeyword,
        AsyncKeyword, HandlerKeyword, AwaitKeyword, AllKeyword, AnyKeyword, KillKeyword, StatusKeyword,
        ExecKeyword,

        /// <summary>组合词素 "$raw"，见 12.1/12.2 节；raw 不是 9.6 节保留关键字，只有和 "$" 相连才有特殊语法意义。</summary>
        DollarRawToken,
        TripleQuoteToken,

        PlusToken, MinusToken, StarToken, SlashToken, PercentToken,
        EqualsToken, EqualsEqualsToken, ExclamationEqualsToken,
        LessThanToken, LessThanEqualsToken, GreaterThanEqualsToken, GreaterThanToken,
        AmpersandToken, PipeToken, CaretToken, ExclamationToken,
        OpenParenToken, CloseParenToken,
        ColonToken, CommaToken,
        AtToken, HashToken,
    }
}
