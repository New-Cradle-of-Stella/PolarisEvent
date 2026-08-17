namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// 延迟视觉访问器的键格式。生成器按 <c>&lt;actorId&gt;/&lt;visualKey&gt;</c> 登记访问器，
    /// 运行时按同一格式回查；两侧必须用同一份规则，否则模组自定义立绘会在演出时静默查不到。
    /// </summary>
    public static class ActorVisualKeys
    {
        public const string WorldSprite = "world";

        public const string Icon = "icon";

        public static string Portrait(string portraitId) => "portrait:" + (portraitId ?? string.Empty);

        public static string UiPortrait(string uiPortraitId) => "ui:" + (uiPortraitId ?? string.Empty);

        /// <summary>按视觉分类拼出对应的键。</summary>
        public static string For(ActorVisualKind kind, string visualId)
        {
            switch (kind)
            {
                case ActorVisualKind.WorldSprite:
                    return WorldSprite;
                case ActorVisualKind.Icon:
                    return Icon;
                case ActorVisualKind.UiPortrait:
                    return UiPortrait(visualId);
                default:
                    return Portrait(visualId);
            }
        }
    }
}
