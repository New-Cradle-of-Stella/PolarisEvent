using System;
using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Core.Tests.Runtime.Fakes
{
    /// <summary>用委托快速拼一个组合协程，免得每个测试都写一个类。</summary>
    public sealed class DelegateRoutine : IPevtCommandRoutine
    {
        private readonly Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> _run;

        public DelegateRoutine(Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> run) => _run = run;

        public IEnumerator<PevtWait> Run(PevtRoutineContext context, PevtArguments arguments) => _run(context, arguments);
    }

    /// <summary>
    /// 内存测试宿主：编译一段 PEVT 源码，接上全套替身服务，按帧推进。
    /// 完全不引用游戏程序集，功能阶段 C 的全部流程都在这里跑完。
    /// </summary>
    public sealed class PevtTestHost
    {
        public FakeClock Clock { get; } = new FakeClock();

        public FakeDialogue Dialogue { get; } = new FakeDialogue();

        public FakeChoice Choice { get; } = new FakeChoice();

        public FakeResources Resources { get; } = new FakeResources();

        public FakePortrait Portrait { get; } = new FakePortrait();

        public FakeStage Stage { get; } = new FakeStage();

        public PevtEventSession Session { get; private set; }

        public PevtServices Services { get; private set; }

        public PevtCommandRegistry Commands { get; }

        public PevtScheduler Scheduler { get; }

        public ActorDirectory Actors { get; set; } = ActorDirectory.Empty;

        public PevtBudgetLimits Limits { get; set; } = PevtBudgetLimits.Default;

        /// <summary>callevt 的目标表。测试可以在事件启动之后再往里加事件，用来验证晚注册。</summary>
        public FakeSubEventProvider SubEvents { get; } = new FakeSubEventProvider();

        public PevtTestHost()
        {
            Commands = new PevtCommandRegistry(CommandDescriptorCatalog.Builtin);
            Scheduler = new PevtScheduler(Clock);
        }

        /// <summary>登记一个同步 <c>@</c> 处理器。类型序列必须与描述目录里的某个重载完全一致。</summary>
        public PevtTestHost Command(string name, IReadOnlyList<PevtType> parameterTypes, Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> run)
        {
            Commands.Register(name, parameterTypes, new DelegateRoutine(run));
            return this;
        }

        /// <summary>把一段源码编译成可执行程序。静态诊断有 Error 时抛出，测试里就是写错了源码。</summary>
        public PevtCompiledProgram Compile(string source, string filePath = "test.pevt")
        {
            SourceText text = SourceText.FromUtf8(new UTF8Encoding(false).GetBytes(source), filePath).Text;
            PevtCompilation compilation = PevtSourceCompiler.Compile(text, CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());

            if (!compilation.Success)
                throw new InvalidOperationException("源码有静态错误：" + string.Join("; ", Describe(compilation.Diagnostics)));

            PevtCompileResult result = PevtCompiledProgram.Compile(compilation.Definition);
            if (!result.Success)
                throw new InvalidOperationException("本阶段尚未支持：" + string.Join("; ", result.UnsupportedFeatures));

            return result.Program;
        }

        /// <summary>只做编译，不要求成功——用于验证"本阶段尚未支持"的构造。</summary>
        public PevtCompileResult TryCompile(string source, string filePath = "test.pevt")
        {
            SourceText text = SourceText.FromUtf8(new UTF8Encoding(false).GetBytes(source), filePath).Text;
            PevtCompilation compilation = PevtSourceCompiler.Compile(text, CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());
            if (!compilation.Success)
                throw new InvalidOperationException("源码有静态错误：" + string.Join("; ", Describe(compilation.Diagnostics)));

            return PevtCompiledProgram.Compile(compilation.Definition);
        }

        public PevtExecution Start(string source, string filePath = "test.pevt") => Start(Compile(source, filePath));

        public PevtExecution Start(PevtCompiledProgram program)
        {
            Session = new PevtEventSession(program.EventId);
            Services = new PevtServices(
                Clock, Session,
                new FakeActorCatalogService(Actors),
                Resources, Dialogue, Choice, Portrait,
                Stage, Stage, Stage, Stage, Stage, Stage, Stage, Stage);

            return new PevtExecution(program, Services, Commands, Limits) { SubEvents = SubEvents };
        }

        /// <summary>推进最多 <paramref name="maxFrames"/> 帧，直到执行结束。</summary>
        public PevtExecutionResult RunToCompletion(PevtExecution execution, int maxFrames = 256)
        {
            PevtExecutionResult result = null;
            for (int i = 0; i < maxFrames; i++)
            {
                result = execution.Resume();
                if (execution.IsFinished)
                    return result;
                Clock.Advance();
            }

            throw new InvalidOperationException($"执行在 {maxFrames} 帧内没有结束，最后状态 {result}。");
        }

        /// <summary>把一段源码编译好登记进 callevt 目标表。</summary>
        public PevtTestHost Event(string eventId, string source)
        {
            SubEvents.Add(eventId, Compile(source, eventId + ".pevt"));
            return this;
        }

        /// <summary>推进一帧并让时钟前进，返回本帧结果。</summary>
        public PevtExecutionResult Step(PevtExecution execution)
        {
            PevtExecutionResult result = execution.Resume();
            Clock.Advance();
            return result;
        }

        private static IEnumerable<string> Describe(IEnumerable<Polaris.Pevt.Diagnostics.Diagnostic> diagnostics)
        {
            foreach (Polaris.Pevt.Diagnostics.Diagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == Polaris.Pevt.Diagnostics.DiagnosticSeverity.Error)
                    yield return diagnostic.ToString();
            }
        }
    }
}
