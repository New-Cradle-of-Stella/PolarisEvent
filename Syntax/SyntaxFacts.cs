using System.Collections.Generic;
using System.Linq;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 关键字、类型名、enable 能力与保留字表。保留字表与关键字表使用同一份名称集合：保留关键字就是词法关键字本身。
    /// </summary>
    public static class SyntaxFacts
    {
        public static IReadOnlyDictionary<string, SyntaxKind> Keywords { get; } = new Dictionary<string, SyntaxKind>
        {
            ["id"] = SyntaxKind.IdKeyword,
            ["enable"] = SyntaxKind.EnableKeyword,
            ["cs"] = SyntaxKind.CsKeyword,
            ["cmd"] = SyntaxKind.CmdKeyword,
            ["cmdarg"] = SyntaxKind.CmdArgKeyword,
            ["end"] = SyntaxKind.EndKeyword,
            ["block"] = SyntaxKind.BlockKeyword,
            ["callevt"] = SyntaxKind.CallEvtKeyword,
            ["if"] = SyntaxKind.IfKeyword,
            ["elif"] = SyntaxKind.ElifKeyword,
            ["else"] = SyntaxKind.ElseKeyword,
            ["endif"] = SyntaxKind.EndIfKeyword,
            ["ifdef"] = SyntaxKind.IfDefKeyword,
            ["elsedef"] = SyntaxKind.ElseDefKeyword,
            ["endifdef"] = SyntaxKind.EndIfDefKeyword,
            ["while"] = SyntaxKind.WhileKeyword,
            ["endwhile"] = SyntaxKind.EndWhileKeyword,
            ["switch"] = SyntaxKind.SwitchKeyword,
            ["case"] = SyntaxKind.CaseKeyword,
            ["default"] = SyntaxKind.DefaultKeyword,
            ["endswitch"] = SyntaxKind.EndSwitchKeyword,
            ["goto"] = SyntaxKind.GotoKeyword,
            ["var"] = SyntaxKind.VarKeyword,
            ["const"] = SyntaxKind.ConstKeyword,
            ["int"] = SyntaxKind.IntKeyword,
            ["float"] = SyntaxKind.FloatKeyword,
            ["bool"] = SyntaxKind.BoolKeyword,
            ["char"] = SyntaxKind.CharKeyword,
            ["string"] = SyntaxKind.StringKeyword,
            ["true"] = SyntaxKind.TrueKeyword,
            ["false"] = SyntaxKind.FalseKeyword,
            ["return"] = SyntaxKind.ReturnKeyword,
            ["endblock"] = SyntaxKind.EndBlockKeyword,
            ["async"] = SyntaxKind.AsyncKeyword,
            ["handler"] = SyntaxKind.HandlerKeyword,
            ["await"] = SyntaxKind.AwaitKeyword,
            ["all"] = SyntaxKind.AllKeyword,
            ["any"] = SyntaxKind.AnyKeyword,
            ["kill"] = SyntaxKind.KillKeyword,
            ["status"] = SyntaxKind.StatusKeyword,
            ["exec"] = SyntaxKind.ExecKeyword,
            ["schedule"] = SyntaxKind.ScheduleKeyword,
            ["after"] = SyntaxKind.AfterKeyword,
            ["call"] = SyntaxKind.CallKeyword,
            ["flush"] = SyntaxKind.FlushKeyword,
            ["clear"] = SyntaxKind.ClearKeyword,
            ["schedules"] = SyntaxKind.SchedulesKeyword,
            ["resources"] = SyntaxKind.ResourcesKeyword,
        };

        /// <summary>9.6 节保留关键字表，与 <see cref="Keywords"/> 的名称集合完全相同。</summary>
        public static IReadOnlyCollection<string> ReservedWords { get; } = Keywords.Keys.ToArray();

        /// <summary>8.2 节允许的全部变量/常量类型名。</summary>
        public static IReadOnlyCollection<string> TypeNames { get; } = new[] { "int", "float", "bool", "char", "string" };

        /// <summary>2.1 节文件级能力声明允许的能力名称。</summary>
        public static IReadOnlyCollection<string> EnableCapabilities { get; } = new[] { "cs", "async", "cmdarg" };

        public static bool TryGetKeywordKind(string text, out SyntaxKind kind) => Keywords.TryGetValue(text, out kind);

        public static bool IsReservedWord(string text) => Keywords.ContainsKey(text);

        public static bool IsTypeName(string text) => TypeNames.Contains(text);

        public static bool IsEnableCapability(string text) => EnableCapabilities.Contains(text);
    }
}
