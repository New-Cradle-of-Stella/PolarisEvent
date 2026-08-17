using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Polaris.Pevt.Runtime
{
    /// <summary>所有权树节点的种类。</summary>
    public enum PevtOwnershipKind
    {
        /// <summary>根事件执行实例。</summary>
        RootEvent,

        /// <summary>同步指令帧（一次 <c>@</c> 调用）。</summary>
        CommandFrame,

        /// <summary>子例程（同步子帧或异步子协程）。</summary>
        Routine,

        /// <summary>一个跨帧等待。</summary>
        Wait,

        /// <summary>一次资源占用或临时显示实例。</summary>
        Resource,
    }

    /// <summary>
    /// 所有权树里的一个节点。根事件、同步例程、子例程、等待与资源占用统一登记在同一棵树上，
    /// 因此"事件结束时到底还剩什么没清理"是一个可以直接查询的事实，而不是各处分散的约定。
    /// </summary>
    public sealed class PevtOwnershipNode
    {
        private readonly List<PevtOwnershipNode> _children = new List<PevtOwnershipNode>();

        public long Id { get; }

        public PevtOwnershipKind Kind { get; }

        public string Description { get; }

        public PevtOwnershipNode Parent { get; internal set; }

        public bool IsReleased { get; internal set; }

        internal Action Release { get; }

        internal PevtOwnershipNode(long id, PevtOwnershipKind kind, string description, Action release)
        {
            Id = id;
            Kind = kind;
            Description = description ?? string.Empty;
            Release = release;
        }

        public IReadOnlyList<PevtOwnershipNode> Children => new ReadOnlyCollection<PevtOwnershipNode>(_children);

        internal List<PevtOwnershipNode> MutableChildren => _children;

        /// <summary>本节点及其全部后代中尚未释放的数量。</summary>
        public int LiveCount
        {
            get
            {
                int count = IsReleased ? 0 : 1;
                foreach (PevtOwnershipNode child in _children)
                    count += child.LiveCount;
                return count;
            }
        }

        public override string ToString() =>
            $"{Kind}#{Id} {Description}{(IsReleased ? " (released)" : string.Empty)}";
    }

    /// <summary>
    /// 事件所有权树。释放一个节点时级联释放它的全部后代，并按登记的逆序执行——
    /// 后建立的资源先释放，与临时清理栈的后进先出规则一致。
    /// </summary>
    public sealed class PevtOwnershipTree
    {
        private long _nextId;
        private readonly List<PevtOwnershipNode> _roots = new List<PevtOwnershipNode>();

        public IReadOnlyList<PevtOwnershipNode> Roots => new ReadOnlyCollection<PevtOwnershipNode>(_roots);

        public PevtOwnershipNode CreateRoot(string description, Action release = null)
        {
            var node = new PevtOwnershipNode(++_nextId, PevtOwnershipKind.RootEvent, description, release);
            _roots.Add(node);
            return node;
        }

        public PevtOwnershipNode Add(PevtOwnershipNode parent, PevtOwnershipKind kind, string description, Action release = null)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            var node = new PevtOwnershipNode(++_nextId, kind, description, release) { Parent = parent };
            parent.MutableChildren.Add(node);
            return node;
        }

        /// <summary>
        /// 级联释放：后代先于自身释放，同层按登记逆序。
        /// 单个释放动作抛异常不阻止其余动作，全部异常收集后返回，由调用方转成 PEVTR1101 附加诊断。
        /// </summary>
        public IReadOnlyList<Exception> ReleaseCascade(PevtOwnershipNode node)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            var failures = new List<Exception>();
            ReleaseRecursive(node, failures);

            if (node.Parent != null)
                node.Parent.MutableChildren.Remove(node);
            else
                _roots.Remove(node);

            return new ReadOnlyCollection<Exception>(failures);
        }

        /// <summary>释放全部根节点，用于插件卸载。</summary>
        public IReadOnlyList<Exception> ReleaseAll()
        {
            var failures = new List<Exception>();
            for (int i = _roots.Count - 1; i >= 0; i--)
                ReleaseRecursive(_roots[i], failures);

            _roots.Clear();
            return new ReadOnlyCollection<Exception>(failures);
        }

        private static void ReleaseRecursive(PevtOwnershipNode node, List<Exception> failures)
        {
            List<PevtOwnershipNode> children = node.MutableChildren;
            for (int i = children.Count - 1; i >= 0; i--)
                ReleaseRecursive(children[i], failures);
            children.Clear();

            if (node.IsReleased)
                return;

            node.IsReleased = true;
            if (node.Release == null)
                return;

            try
            {
                node.Release();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }
    }
}
