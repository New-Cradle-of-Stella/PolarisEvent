using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// 一个已校验的人物资料，深不可变：构造函数把传入集合复制成只读快照，元素类型本身也不可变。
    /// </summary>
    public sealed class ActorDefinition
    {
        /// <summary>目录命名空间内唯一的局部 ID，不含命名空间前缀。</summary>
        public string LocalId { get; }

        /// <summary>本地化键，优先于 <see cref="DisplayName"/>；未声明时为 null。</summary>
        public string DisplayKey { get; }

        /// <summary><see cref="DisplayKey"/> 查不到时使用的直接显示名；未声明时为 null。</summary>
        public string DisplayName { get; }

        public string Voice { get; }

        public ActorColor? Color { get; }

        /// <summary>人物图标资源；未声明时为 null。</summary>
        public ActorVisualResource Icon { get; }

        /// <summary>默认 portrait 的局部 ID；纯对话 profile 没有任何视觉资源时为 null。</summary>
        public string DefaultPortraitId { get; }

        /// <summary>Actor 级原版短键，只用于 <c>_</c> 这类没有 portrait 的固定说话人；未声明时为 null。</summary>
        public string LegacyPerson { get; }

        /// <summary>地图像素角色；最多一个，未声明时为 null。</summary>
        public ActorVisual WorldSprite { get; }

        public IReadOnlyList<ActorVisual> Portraits { get; }

        public IReadOnlyList<ActorVisual> UiPortraits { get; }

        public IReadOnlyList<ActorAppearance> Appearances { get; }

        public IReadOnlyList<ActorAnchor> Anchors { get; }

        public TextLocation Location { get; }

        private readonly Dictionary<string, ActorVisual> _portraitsById;
        private readonly Dictionary<string, ActorVisual> _uiPortraitsById;
        private readonly Dictionary<string, ActorAppearance> _appearancesById;
        private readonly Dictionary<string, ActorAnchor> _anchorsById;

        public ActorDefinition(
            string localId,
            string displayKey = null,
            string displayName = null,
            string voice = null,
            ActorColor? color = null,
            ActorVisualResource icon = null,
            string defaultPortraitId = null,
            string legacyPerson = null,
            ActorVisual worldSprite = null,
            IEnumerable<ActorVisual> portraits = null,
            IEnumerable<ActorVisual> uiPortraits = null,
            IEnumerable<ActorAppearance> appearances = null,
            IEnumerable<ActorAnchor> anchors = null,
            TextLocation location = null)
        {
            if (string.IsNullOrEmpty(localId))
                throw new ArgumentException("人物局部 ID 不能为空。", nameof(localId));
            if (string.IsNullOrEmpty(displayKey) && string.IsNullOrEmpty(displayName))
                throw new ArgumentException("DisplayName 与 DisplayKey 至少要有一个。", nameof(displayName));

            LocalId = localId;
            DisplayKey = displayKey;
            DisplayName = displayName;
            Voice = voice;
            Color = color;
            Icon = icon;
            LegacyPerson = legacyPerson;
            WorldSprite = worldSprite;
            Location = location;

            Portraits = Snapshot(portraits, nameof(portraits));
            UiPortraits = Snapshot(uiPortraits, nameof(uiPortraits));
            Appearances = Snapshot(appearances, nameof(appearances));
            Anchors = Snapshot(anchors, nameof(anchors));

            _portraitsById = Index(Portraits, visual => visual.Id, "portrait");
            _uiPortraitsById = Index(UiPortraits, visual => visual.Id, "ui-portrait");
            _appearancesById = Index(Appearances, appearance => appearance.Id, "appearance");
            _anchorsById = Index(Anchors, anchor => anchor.Id, "anchor");

            if (defaultPortraitId != null && !_portraitsById.ContainsKey(defaultPortraitId))
                throw new ArgumentException($"默认 portrait `{defaultPortraitId}` 不存在。", nameof(defaultPortraitId));
            DefaultPortraitId = defaultPortraitId;

            foreach (ActorAppearance appearance in Appearances)
            {
                if (!_portraitsById.ContainsKey(appearance.PortraitId))
                    throw new ArgumentException($"appearance `{appearance.Id}` 引用了不存在的 portrait `{appearance.PortraitId}`。", nameof(appearances));
            }
        }

        private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> items, string parameterName)
        {
            if (items == null)
                return Array.AsReadOnly(Array.Empty<T>());

            var copy = new List<T>();
            foreach (T item in items)
            {
                if (item == null)
                    throw new ArgumentException("集合中不能包含 null 元素。", parameterName);
                copy.Add(item);
            }

            return new ReadOnlyCollection<T>(copy);
        }

        private static Dictionary<string, T> Index<T>(IReadOnlyList<T> items, Func<T, string> keySelector, string category)
        {
            var map = new Dictionary<string, T>(ActorNaming.IdComparer);
            foreach (T item in items)
            {
                string key = keySelector(item);
                if (map.ContainsKey(key))
                    throw new ArgumentException($"{category} ID `{key}` 在同一人物内重复。");
                map[key] = item;
            }

            return map;
        }

        /// <summary>默认 portrait；纯对话 profile 返回 null。</summary>
        public ActorVisual DefaultPortrait =>
            DefaultPortraitId != null && _portraitsById.TryGetValue(DefaultPortraitId, out ActorVisual portrait) ? portrait : null;

        public bool TryGetPortrait(string id, out ActorVisual portrait) =>
            _portraitsById.TryGetValue(id ?? string.Empty, out portrait);

        public bool TryGetUiPortrait(string id, out ActorVisual uiPortrait) =>
            _uiPortraitsById.TryGetValue(id ?? string.Empty, out uiPortrait);

        public bool TryGetAppearance(string id, out ActorAppearance appearance) =>
            _appearancesById.TryGetValue(id ?? string.Empty, out appearance);

        public bool TryGetAnchor(string id, out ActorAnchor anchor) =>
            _anchorsById.TryGetValue(id ?? string.Empty, out anchor);

        public override string ToString() => LocalId;
    }
}
