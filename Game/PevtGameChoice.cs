using System;
using System.Collections.Generic;
using evt;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 选择适配器，直接驱动原版 <see cref="EvSelector"/>。
    /// </summary>
    internal sealed class PevtGameChoice : IPevtChoice
    {
        /// <summary>占位的 define 目标；只用来让原版把结果当值而不是跳转标签。</summary>
        private const string ResultSlot = "__pevt_choice";

        /// <summary>按加入顺序记录选项键，用来把原版返回的键换算成从 1 开始的序号。</summary>
        private readonly List<string> _keys = new List<string>();

        private string _initialKey;

        public void Reset()
        {
            _keys.Clear();
            _initialKey = null;
            PevtGameHost.Guard("ChoiceReset", () => PevtGameHost.Selector?.clear());
        }

        /// <summary>
        /// 提示文本走普通对话框：原版选择框本身没有标题区，<c>SELECT</c> 之前也总是先出一句消息。
        /// prompt 为空时不打扰当前已经显示的消息。
        /// </summary>
        public void Begin(string prompt)
        {
            _keys.Clear();
            _initialKey = null;

            PevtGameHost.Guard("ChoiceBegin", () =>
            {
                EvSelector selector = PevtGameHost.Selector;
                if (selector == null)
                    return;

                selector.clear();
                selector.clms = 0;
            });

            if (!string.IsNullOrEmpty(prompt))
                PromptText = prompt;
        }

        /// <summary>由服务工厂接上对话适配器，让 <c>@choose</c> 的提示语走同一个消息框。</summary>
        public Action<string> ShowPrompt { get; set; }

        private string PromptText
        {
            set => ShowPrompt?.Invoke(value);
        }

        public void AddIndex(string text)
        {
            // 序号式选择没有稳定键，用位置生成一个内部键；对外仍然返回从 1 开始的序号。
            string key = "__i" + (_keys.Count + 1);
            AddRow(key, text, true, false);
        }

        public void Add(string key, string text, bool enabled, bool cancel) =>
            AddRow(key, text, enabled, cancel);

        private void AddRow(string key, string text, bool enabled, bool cancel)
        {
            PevtGameHost.Guard("ChoiceAdd", () =>
            {
                EvSelector selector = PevtGameHost.Selector;
                if (selector == null)
                    return;

                // 原版用 flags 串表达"取消项"与"锁定项"：C 是取消，L 是不可选。
                string flags = (cancel ? "C" : string.Empty) + (enabled ? string.Empty : "L");
                selector.addRow(text ?? string.Empty, key, flags, false);
            });

            _keys.Add(key);
        }

        public void SetInitial(string initialKey) => _initialKey = initialKey;

        public PevtWait<int> PresentIndex()
        {
            var keys = new List<string>(_keys);
            return new PevtChoiceWait<int>(this, result =>
            {
                int index = keys.IndexOf(result);
                return index < 0 ? 0 : index + 1;
            });
        }

        public PevtWait<string> PresentKey() =>
            new PevtChoiceWait<string>(this, result => result);

        private bool Activate()
        {
            bool activated = false;
            PevtGameHost.Guard("ChoiceActivate", () =>
            {
                EvSelector selector = PevtGameHost.Selector;
                if (selector == null || selector.countRows() == 0)
                    return;

                if (!string.IsNullOrEmpty(_initialKey))
                    selector.setDefaultFocus(_initialKey);

                activated = selector.activate(ResultSlot);
            });

            return activated;
        }

        private static string PollResult()
        {
            string result = PevtGameHost.Safe(() => PevtGameHost.Selector?.result, null);
            return string.IsNullOrEmpty(result) ? null : result;
        }

        private static void Dismiss() =>
            PevtGameHost.Guard("ChoiceDismiss", () => PevtGameHost.Selector?.deactivate());

        /// <summary>
        /// 选择等待。激活失败（一条选项都没有）直接以 PEVTR4001 结束，而不是永远挂着——
        /// 停滞检测只能报"没有推进源"，定位不到真正的原因。
        /// </summary>
        private sealed class PevtChoiceWait<T> : PevtWait<T>
        {
            private readonly PevtGameChoice _choice;
            private readonly Func<string, T> _project;
            private bool _activated;

            public PevtChoiceWait(PevtGameChoice choice, Func<string, T> project)
            {
                _choice = choice;
                _project = project;
            }

            public override string ProgressSource => "选择 UI";

            protected override void OnTick(PevtWaitContext context)
            {
                if (!_activated)
                {
                    if (!_choice.Activate())
                    {
                        CompleteFaulted(new PevtRuntimeDiagnostic("PEVTR4001", "选择没有任何可用选项，无法展示。"));
                        return;
                    }

                    _activated = true;
                    return;
                }

                string result = PollResult();
                if (result == null)
                    return;

                Dismiss();
                CompleteSucceeded(_project(result));
            }

            protected override void OnCancelRequested()
            {
                if (_activated)
                    Dismiss();
            }
        }
    }
}
