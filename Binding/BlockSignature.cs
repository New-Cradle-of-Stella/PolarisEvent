using System.Collections.Generic;

namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 14.1 节自定义事件块已完成解析的签名，从 <c>BlockDefinitionStatementSyntax</c> 提炼出来，
    /// 供后续调用点做"定义先于调用"、参数匹配和返回类型核对（14.4 节）。只在整段定义（含配对的
    /// <c>endblock</c>）绑定完毕后才登记进 <see cref="Binder"/> 的就绪表——这正是"定义先于调用"
    /// 检查（PEVT7115）天然成立的原因，不需要额外的可达性分析。
    /// </summary>
    public sealed class BlockSignature
    {
        public string Name { get; }
        public bool IsAsync { get; }
        public IReadOnlyList<PevtType> ParameterTypes { get; }
        public PevtType? ReturnType { get; }

        public BlockSignature(string name, bool isAsync, IReadOnlyList<PevtType> parameterTypes, PevtType? returnType)
        {
            Name = name;
            IsAsync = isAsync;
            ParameterTypes = parameterTypes;
            ReturnType = returnType;
        }
    }
}
