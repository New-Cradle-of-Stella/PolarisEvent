using UnityEngine;

namespace Polaris.Event.Game.Debugging
{
    /// <summary>
    /// 调试页的 Unity 宿主。IMGUI 只在 <c>MonoBehaviour.OnGUI</c> 里可用，而 Polaris 组件是普通类库，
    /// 所以整个类只做一件事：把 OnGUI 转给 <see cref="PevtDebugPage"/>。热键与开关仍由组件的更新点驱动。
    /// </summary>
    internal sealed class PevtDebugOverlay : MonoBehaviour
    {
        private void OnGUI() => PevtDebugPage.Draw();
    }
}
