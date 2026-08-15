using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Actors
{
    /// <summary>参数域取值的解析结果。三种状态都不产生静态诊断，只供工具展示与运行期决策。</summary>
    public enum ActorParameterStatus
    {
        /// <summary>在当前可见的人物空间中找到了该取值。</summary>
        Known,

        /// <summary>格式合法但当前看不到。跨模组人物可以晚于本项目注册，因此这不是错误。</summary>
        Unknown,

        /// <summary>取值本身不符合该域的书写格式，例如人物 ID 缺少命名空间。</summary>
        Malformed,
    }

    /// <summary>
    /// 内置语义站位。普通 <c>@actor_enter</c> / <c>@actor_move</c> 使用这些名字，
    /// 坐标由 PolarisEvent 自己保存，不在运行时翻译回原版 <c>L/R/C</c> 组合键
    /// （PEVT-人物目录与原版别名规范.md 第 8 节）。
    /// </summary>
    public static class BuiltinActorAnchors
    {
        private static readonly string[] Names =
        {
            "left",
            "center",
            "right",
            "near-left",
            "near-right",
            "far-left",
            "far-right",
            "off-left",
            "off-right",
        };

        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Names);

        public static bool Contains(string name)
        {
            if (name == null)
                return false;
            foreach (string candidate in Names)
            {
                if (string.Equals(candidate, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 把人物相关参数域的取值解析到一个已合并的人物空间上。
    ///
    /// 本类型只回答"当前看得见什么"，永远不发射诊断：人物、外观和站位的存在性通常是运行期事实，
    /// 未知取值只影响补全，不产生静态错误。运行期缺失由 PEVTR4401/PEVTR4402 负责。
    /// </summary>
    public sealed class ActorParameterResolver
    {
        private readonly ActorDirectory _directory;

        public ActorParameterResolver(ActorDirectory directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        /// <summary>
        /// 判断一个字面量取值在当前人物空间中的状态。
        /// <paramref name="actorId"/> 是同一次调用中已经写好的人物实参，
        /// appearance / portrait / anchor 域需要它才能确定候选范围。
        /// </summary>
        public ActorParameterStatus Check(ParameterDomain domain, string value, string actorId = null)
        {
            if (domain == null)
                throw new ArgumentNullException(nameof(domain));
            if (value == null)
                return ActorParameterStatus.Malformed;

            if (domain == ParameterDomain.ActorId)
            {
                if (!ActorNaming.IsValidActorId(value))
                    return ActorParameterStatus.Malformed;
                return _directory.Contains(value) ? ActorParameterStatus.Known : ActorParameterStatus.Unknown;
            }

            if (domain == ParameterDomain.ActorAnchor && BuiltinActorAnchors.Contains(value))
                return ActorParameterStatus.Known;

            if (!ActorNaming.IsValidLocalId(value))
                return ActorParameterStatus.Malformed;

            // 人物未知时无法判断其下的 appearance/portrait/anchor，一律按 Unknown 处理。
            if (actorId == null || !_directory.TryGetActor(actorId, out ActorRegistration registration))
                return ActorParameterStatus.Unknown;

            ActorDefinition actor = registration.Actor;
            bool found;
            if (domain == ParameterDomain.ActorAppearance)
                found = actor.TryGetAppearance(value, out _);
            else if (domain == ParameterDomain.ActorPortrait)
                found = actor.TryGetPortrait(value, out _);
            else if (domain == ParameterDomain.ActorUiPortrait)
                found = actor.TryGetUiPortrait(value, out _);
            else if (domain == ParameterDomain.ActorAnchor)
                found = actor.TryGetAnchor(value, out _);
            else
                return ActorParameterStatus.Unknown;

            return found ? ActorParameterStatus.Known : ActorParameterStatus.Unknown;
        }

        /// <summary>补全候选。列表顺序稳定：内置站位在前，人物专用 anchor 在后。</summary>
        public IReadOnlyList<string> Complete(ParameterDomain domain, string actorId = null)
        {
            if (domain == null)
                throw new ArgumentNullException(nameof(domain));

            var result = new List<string>();

            if (domain == ParameterDomain.ActorId)
            {
                foreach (ActorRegistration registration in _directory.Actors)
                {
                    if (_directory.Contains(registration.ActorId))
                        result.Add(registration.ActorId);
                }

                return new ReadOnlyCollection<string>(result);
            }

            if (domain == ParameterDomain.ActorAnchor)
                result.AddRange(BuiltinActorAnchors.All);

            if (actorId == null || !_directory.TryGetActor(actorId, out ActorRegistration actorRegistration))
                return new ReadOnlyCollection<string>(result);

            ActorDefinition actor = actorRegistration.Actor;
            if (domain == ParameterDomain.ActorAppearance)
            {
                foreach (ActorAppearance appearance in actor.Appearances)
                    result.Add(appearance.Id);
            }
            else if (domain == ParameterDomain.ActorPortrait)
            {
                foreach (ActorVisual portrait in actor.Portraits)
                    result.Add(portrait.Id);
            }
            else if (domain == ParameterDomain.ActorUiPortrait)
            {
                foreach (ActorVisual uiPortrait in actor.UiPortraits)
                    result.Add(uiPortrait.Id);
            }
            else if (domain == ParameterDomain.ActorAnchor)
            {
                foreach (ActorAnchor anchor in actor.Anchors)
                    result.Add(anchor.Id);
            }

            return new ReadOnlyCollection<string>(result);
        }
    }
}
