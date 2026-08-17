using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Runtime.Raw;

namespace Polaris.Pevt.Runtime
{
    // 两个受控逃生口的执行入口。
    public sealed partial class PevtExecution
    {
        /// <summary>
        /// <c>$raw cmd</c>：把原始文本交给进程级原版会话通道，然后挂在它的等待上。
        /// </summary>
        private PevtExecutionResult ExecuteRawCmd(PevtFrame frame, PevtInstruction instruction)
        {
            IPevtRawCommands channel = _services.RawCommands;
            if (channel == null)
            {
                // 编译出了 RawCmd 指令，宿主却没有接通道——这是宿主接线的不变量被破坏，
                // 不是脚本的错，也不是原版拒绝了文本，所以不能用 PEVTR4101 冒充。
                return Fault("PEVTR9001",
                    "当前宿主没有登记 `$raw cmd` 通道，无法进入原版 EV 文本会话。", instruction.Span);
            }

            string rawText = instruction.Constant.Type == PevtType.String ? instruction.Constant.AsString : string.Empty;

            PevtWait wait;
            try
            {
                wait = channel.Submit(rawText);
            }
            catch (Exception ex)
            {
                return Fault(new PevtRuntimeDiagnostic("PEVTR4101",
                    $"提交 `$raw cmd` 失败：{ex.GetType().Name}: {ex.Message}",
                    LocationOf(instruction.Span), BuildCallStack(), innerException: ex));
            }

            return Suspend(wait, () =>
            {
                frame.Ip++;
                return null;
            });
        }

        /// <summary>
        /// <c>$raw cs</c>：按名字取传入变量的值快照，交给受信任执行器同步执行。
        /// </summary>
        private PevtExecutionResult ExecuteRawCs(PevtFrame frame, PevtInstruction instruction)
        {
            if (!_program.HasCsCapability)
            {
                // 静态门（PEVT8015）本该在加载期拦住它；走到这里说明宿主没有接 C# 分析器，
                // 而运行时仍然必须拒绝，不能因为"静态没查出来"就放行任意 C#。
                return Fault("PEVTR9001",
                    "当前事件没有声明 `enable cs`，不能执行 `$raw cs`。", instruction.Span);
            }

            PevtRawCsExecutor executor = _services.RawCs;
            if (executor == null)
            {
                return Fault("PEVTR9001",
                    "当前宿主没有登记 `$raw cs` 执行器。", instruction.Span);
            }

            var parameters = new List<PevtRawCsParameter>();
            var values = new List<PevtValue>();

            IReadOnlyList<string> names = instruction.Names ?? Array.Empty<string>();
            foreach (string name in names)
            {
                if (!frame.Environment.TryGetSlot(name, out PevtSlot slot))
                    return Fault("PEVTR9001", $"环境 `{frame.Environment.ScopeName}` 中不存在名称 `{name}`。", instruction.Span);

                if (!slot.IsInitialized)
                    return Fault("PEVTR3002", $"`$raw cs` 传入了尚未初始化的 `{name}`。", instruction.Span);

                parameters.Add(new PevtRawCsParameter(name, slot.DeclaredType));
                values.Add(slot.Value);
            }

            PevtRawCsResult result;
            try
            {
                // 请求构造也放在 try 里：重复的传入变量名会让它抛 ArgumentException，
                // 而那种源码本该在加载期被 PEVT8013 拦住——真漏过来时也必须变成诊断，不能穿出解释器。
                var request = new PevtRawCsRequest(
                    instruction.Constant.Type == PevtType.String ? instruction.Constant.AsString : string.Empty,
                    parameters,
                    instruction.Flag ? PevtRawCsUsage.Expression : PevtRawCsUsage.Statement);

                result = executor.Execute(request, values);
            }
            catch (PevtNullResultException ex)
            {
                return Fault(new PevtRuntimeDiagnostic("PEVTR3003",
                    "`$raw cs` 向不支持 `null` 的 PEVT 普通类型返回了空值。",
                    LocationOf(instruction.Span), BuildCallStack(), innerException: ex));
            }
            catch (PevtRawCsException ex)
            {
                return Fault(new PevtRuntimeDiagnostic("PEVTR4102", ex.Message,
                    LocationOf(instruction.Span), BuildCallStack(), innerException: ex.InnerException ?? ex));
            }
            catch (Exception ex)
            {
                return Fault(new PevtRuntimeDiagnostic("PEVTR4102",
                    $"`$raw cs` 执行失败：{ex.GetType().Name}: {ex.Message}",
                    LocationOf(instruction.Span), BuildCallStack(), innerException: ex));
            }

            if (!instruction.Flag)
            {
                // 语句位置：有返回值也丢弃（12.3 节"返回值被赋给变量或常量时保存快照"的反面）。
                frame.Ip++;
                return null;
            }

            if (!result.HasValue)
            {
                // 静态上这是 PEVT8006；没有分析器时只能在这里拦住。
                return Fault("PEVTR4102",
                    "`$raw cs` 没有返回值，不能用作 PEVT 表达式。", instruction.Span);
            }

            frame.EvalStack.Add(result.Value);
            frame.Ip++;
            return null;
        }
    }
}
