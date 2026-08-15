using System;
using System.Collections.Generic;
using System.Linq;

namespace Polaris.Pevt.Binding
{
    /// <summary>11.2 节 API 签名里的一个形参：名称 + 后置类型。</summary>
    public sealed class BuiltinParameter
    {
        public string Name { get; }
        public PevtType Type { get; }

        public BuiltinParameter(string name, PevtType type)
        {
            Name = name;
            Type = type;
        }
    }

    /// <summary>
    /// 11.2 节的一条 <c>@</c> API 签名：宿主侧注册契约，不是 <c>.pevt</c> 源文件里的自举定义语法。
    /// 本阶段只搭建"绑定器如何按名称和参数签名匹配调用"的机制本身；把游戏侧真实处理器登记进一张
    /// 全局表是阶段 13"@ API 描述目录"的工作，那时会有真正的 CommandDescriptor 数据喂给这里。
    /// </summary>
    public sealed class BuiltinSignature
    {
        public string Name { get; }
        public bool IsAsync { get; }
        public IReadOnlyList<BuiltinParameter> Parameters { get; }

        /// <summary>null 表示 11.2 节"API 没有书写返回值类型"的纯调用。</summary>
        public PevtType? ReturnType { get; }

        public BuiltinSignature(string name, bool isAsync, IReadOnlyList<BuiltinParameter> parameters, PevtType? returnType)
        {
            Name = name;
            IsAsync = isAsync;
            Parameters = parameters;
            ReturnType = returnType;
        }

        /// <summary>11.2 节："参数类型和返回值类型只能是 int、float、bool、char 或 string"
        /// （PEVT7010 的判定依据——校验的是 API 表条目自身，不是某一次具体调用）。</summary>
        public bool HasValidSignatureShape() =>
            Parameters.All(p => p.Type.IsOrdinaryType()) && (!ReturnType.HasValue || ReturnType.Value.IsOrdinaryType());
    }

    /// <summary>按名称分组的 <c>@</c> API 表；同名可以有多条签名（重载，按参数数量和类型区分，11.2 节）。</summary>
    public sealed class BuiltinApiTable
    {
        private readonly Dictionary<string, List<BuiltinSignature>> _byName = new Dictionary<string, List<BuiltinSignature>>();

        /// <summary>没有任何登记的空表——本阶段游戏侧尚未接入真实处理器时的默认值。</summary>
        public static BuiltinApiTable Empty => new BuiltinApiTable();

        public void Register(BuiltinSignature signature)
        {
            if (!_byName.TryGetValue(signature.Name, out List<BuiltinSignature> list))
                _byName[signature.Name] = list = new List<BuiltinSignature>();
            list.Add(signature);
        }

        public IReadOnlyList<BuiltinSignature> Find(string name) =>
            _byName.TryGetValue(name, out List<BuiltinSignature> list) ? list : Array.Empty<BuiltinSignature>();
    }
}
