using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary>推进一次指令帧的结果。</summary>
    public enum PevtCommandStep
    {
        /// <summary>当前等待仍未完成。</summary>
        Waiting,

        /// <summary>协程往前走了一步，可以继续。</summary>
        Progressed,

        /// <summary>协程正常结束。</summary>
        Completed,

        /// <summary>协程或等待失败。</summary>
        Faulted,
    }

    /// <summary>
    /// 一次同步 <c>@</c> 调用的临时指令帧。
    /// </summary>
    public sealed class PevtCommandFrame
    {
        private readonly IPevtCommandRoutine _routine;
        private IEnumerator<PevtWait> _enumerator;
        private bool _completed;

        public CommandDescriptor Descriptor { get; }

        public PevtArguments Arguments { get; }

        public PevtRoutineContext Context { get; }

        /// <summary>协程当前产出且尚未处理完成的等待。</summary>
        public PevtWait CurrentWait { get; private set; }

        public TextSpan Span { get; }

        internal PevtCommandFrame(
            CommandDescriptor descriptor,
            PevtArguments arguments,
            IPevtCommandRoutine routine,
            PevtRoutineContext context,
            TextSpan span)
        {
            Descriptor = descriptor;
            Arguments = arguments;
            _routine = routine;
            Context = context;
            Span = span;
        }

        /// <summary>创建协程。工厂本身抛异常时转成 PEVTR4001。</summary>
        internal PevtRuntimeDiagnostic Start()
        {
            try
            {
                _enumerator = _routine.Run(Context, Arguments);
                if (_enumerator == null)
                    return new PevtRuntimeDiagnostic("PEVTR4002", $"`@{Descriptor.Name}` 的处理器没有返回协程。", Context.Location);
            }
            catch (Exception ex)
            {
                return Translate(ex);
            }

            return null;
        }

        /// <summary>
        /// 推进一步。等待未完成时返回 <see cref="PevtCommandStep.Waiting"/>；
        /// 等待成功后从 <c>yield return</c> 之后续跑。
        /// </summary>
        internal PevtCommandStep Advance(PevtWaitContext waitContext, out PevtRuntimeDiagnostic error)
        {
            error = null;

            if (_completed)
                return PevtCommandStep.Completed;

            if (CurrentWait != null)
            {
                try
                {
                    CurrentWait.Tick(waitContext);
                }
                catch (Exception ex)
                {
                    error = Translate(ex);
                    return PevtCommandStep.Faulted;
                }

                switch (CurrentWait.State)
                {
                    case PevtWaitState.Succeeded:
                        CurrentWait = null;
                        break;

                    case PevtWaitState.Faulted:
                        error = CurrentWait.Error;
                        return PevtCommandStep.Faulted;

                    case PevtWaitState.Cancelled:
                        error = new PevtRuntimeDiagnostic("PEVTR4001", $"`@{Descriptor.Name}` 的等待被取消。", Context.Location);
                        return PevtCommandStep.Faulted;

                    default:
                        return PevtCommandStep.Waiting;
                }
            }

            bool moved;
            try
            {
                moved = _enumerator.MoveNext();
            }
            catch (Exception ex)
            {
                error = Translate(ex);
                return PevtCommandStep.Faulted;
            }

            if (!moved)
            {
                _completed = true;
                return PevtCommandStep.Completed;
            }

            PevtWait produced = _enumerator.Current;
            if (produced == null)
            {
                error = new PevtRuntimeDiagnostic("PEVTR9001", $"`@{Descriptor.Name}` 的协程产出了 null 等待。", Context.Location);
                return PevtCommandStep.Faulted;
            }

            if (produced.State != PevtWaitState.Created)
            {
                error = new PevtRuntimeDiagnostic("PEVTR9001",
                    $"`@{Descriptor.Name}` 的协程产出了已被其他协程持有的等待（当前 {produced.State}）。", Context.Location);
                return PevtCommandStep.Faulted;
            }

            produced.Attach();
            CurrentWait = produced;
            return PevtCommandStep.Progressed;
        }

        /// <summary>
        /// 校验返回值契约：纯调用不能提交返回值，有值 <c>@</c> 必须提交且类型必须相符。
        /// 违反时映射 PEVTR4002。
        /// </summary>
        internal PevtRuntimeDiagnostic ValidateResult(out PevtValue result, out bool hasResult)
        {
            result = default;
            hasResult = false;

            PevtResultSink sink = Context.Result;

            if (!Descriptor.ReturnType.HasValue)
            {
                if (sink.HasValue)
                    return new PevtRuntimeDiagnostic("PEVTR4002", $"`@{Descriptor.Name}` 是纯调用，但处理器提交了返回值。", Context.Location);
                return null;
            }

            if (!sink.HasValue)
                return new PevtRuntimeDiagnostic("PEVTR4002", $"`@{Descriptor.Name}` 声明返回 {Descriptor.ReturnType.Value.DisplayName()}，但处理器没有提交返回值。", Context.Location);

            if (sink.Value.Type != Descriptor.ReturnType.Value)
            {
                return new PevtRuntimeDiagnostic("PEVTR4002",
                    $"`@{Descriptor.Name}` 声明返回 {Descriptor.ReturnType.Value.DisplayName()}，处理器提交的是 {sink.Value.Type.DisplayName()}。",
                    Context.Location);
            }

            result = sink.Value;
            hasResult = true;
            return null;
        }

        /// <summary>处置迭代器并逆序执行临时清理。持久状态修改不回滚。</summary>
        internal IReadOnlyList<Exception> Dispose()
        {
            var failures = new List<Exception>();

            try
            {
                _enumerator?.Dispose();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            _enumerator = null;
            failures.AddRange(Context.Cleanup.RunAll());
            return failures;
        }

        /// <summary>取消当前等待后再处置。取消是幂等的。</summary>
        internal IReadOnlyList<Exception> CancelAndDispose()
        {
            Context.IsCancellationRequested = true;

            try
            {
                CurrentWait?.Cancel();
            }
            catch (Exception)
            {
                // 取消失败不阻止后面的处置；具体诊断由调用方按 PEVTR5003 处理。
            }

            CurrentWait = null;
            return Dispose();
        }

        /// <summary>
        /// 把调度边界捕获到的 C# 异常翻译成运行诊断。原子方法、<c>MoveNext</c> 和 <c>Tick</c>
        /// 抛出的异常统一走这里，不允许穿透到宿主。
        /// </summary>
        private PevtRuntimeDiagnostic Translate(Exception exception)
        {
            if (exception is PevtNullResultException)
            {
                return new PevtRuntimeDiagnostic("PEVTR3003",
                    $"`@{Descriptor.Name}` 向不支持 null 的 PEVT 普通类型返回了空值。", Context.Location, innerException: exception);
            }

            // 处理器已经指明了具体编号时原样保留，不降级成笼统的 PEVTR4001。
            if (exception is PevtRoutineFailureException failure)
                return new PevtRuntimeDiagnostic(failure.DiagnosticId, failure.Message, Context.Location, innerException: failure.InnerException);

            return new PevtRuntimeDiagnostic("PEVTR4001",
                $"`@{Descriptor.Name}` 执行失败：{exception.GetType().Name}: {exception.Message}",
                Context.Location, innerException: exception);
        }
    }
}
