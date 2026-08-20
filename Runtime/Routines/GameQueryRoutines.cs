using System;
using System.Collections.Generic;
using System.Globalization;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// PEVT-E01：<c>@game_read_int/float/bool/string</c> 的组合处理器。
    /// 四条指令共用同一个查询步骤，只有"转换成哪个普通类型"不同——转换失败是独立诊断，
    /// 不允许静默给出零值。
    /// </summary>
    internal static class GameQueryRoutines
    {
        public static IEnumerator<PevtWait> GameReadInt(PevtRoutineContext context, PevtArguments args)
        {
            context.Result.SetInt(ReadInt(context, args));
            yield break;
        }

        public static IEnumerator<PevtWait> GameReadFloat(PevtRoutineContext context, PevtArguments args)
        {
            context.Result.SetFloat(ReadFloat(context, args));
            yield break;
        }

        public static IEnumerator<PevtWait> GameReadBool(PevtRoutineContext context, PevtArguments args)
        {
            context.Result.SetBool(ReadBool(context, args));
            yield break;
        }

        public static IEnumerator<PevtWait> GameReadString(PevtRoutineContext context, PevtArguments args)
        {
            context.Result.SetString(ReadString(context, args));
            yield break;
        }

        // ---- 查询 ----

        /// <summary>
        /// 读一次查询表。<c>key</c> 是第 0 个实参，其余实参按顺序作为该键的查询参数——
        /// 它们不会被拼接成一段表达式再求值，服务契约只接受"键 + 参数表"。
        /// </summary>
        private static PevtQueryValue Read(PevtRoutineContext context, PevtArguments args, PevtType targetType)
        {
            string key = args.String(0);
            List<string> arguments = QueryArguments(args);

            if (string.IsNullOrEmpty(key))
                throw Fault(context, "PEVTR4502", key, arguments, targetType, null, "`key` 不能为空。");

            for (int i = 0; i < arguments.Count; i++)
            {
                if (arguments[i] == null)
                    throw Fault(context, "PEVTR4502", key, arguments, targetType, null, $"第 {i + 1} 个查询参数为空引用。");
            }

            IPevtGameQuery query = context.Services.GameQuery;
            if (query == null)
                throw Fault(context, "PEVTR4501", key, arguments, targetType, null, "当前运行时没有接入只读游戏查询表。");

            PevtQueryStatus status;
            PevtQueryValue value;
            try
            {
                status = query.TryRead(key, arguments, out value);
            }
            catch (PevtRoutineFailureException)
            {
                // 适配器已经选好了自己的诊断编号，原样放行。
                throw;
            }
            catch (Exception ex)
            {
                throw Fault(context, "PEVTR4501", key, arguments, targetType, null,
                    $"只读查询表在读取 `{key}` 时抛出 {ex.GetType().Name}: {ex.Message}", ex);
            }

            switch (status)
            {
                case PevtQueryStatus.UnknownKey:
                    throw Fault(context, "PEVTR4501", key, arguments, targetType, null,
                        $"只读查询表里没有登记键 `{key}`。");
                case PevtQueryStatus.InvalidArguments:
                    throw Fault(context, "PEVTR4502", key, arguments, targetType, null,
                        $"键 `{key}` 不接受给定的 {arguments.Count} 个查询参数。");
            }

            if (value.Kind == PevtQueryValueKind.Text && value.Text == null)
                throw Fault(context, "PEVTR4503", key, arguments, targetType, null, $"键 `{key}` 回报了空文本结果。");

            PevtGameQueryLog.Shared.Add(PevtGameQueryTrace.Succeeded(
                Frame(context), context.EventId, key, arguments, targetType, value));
            return value;
        }

        /// <summary>
        /// 数值结果按"必须本身就是整数"处理。查询表统一用 <c>double</c> 回报，所以整数键取到的是
        /// <c>3.0</c> 这种精确值；直接截断会把 <c>2.9</c> 静默变成 <c>2</c>，那是脚本作者最难发现的一类错。
        /// </summary>
        private static int ReadInt(PevtRoutineContext context, PevtArguments args)
        {
            PevtQueryValue value = Read(context, args, PevtType.Int);

            if (value.Kind == PevtQueryValueKind.Number)
            {
                double number = value.Number;
                double rounded = Math.Round(number, MidpointRounding.AwayFromZero);

                if (double.IsNaN(number) || Math.Abs(number - rounded) > 1e-9d)
                    throw ConversionFault(context, args, PevtType.Int, value, "结果不是整数值。");
                if (rounded < int.MinValue || rounded > int.MaxValue)
                    throw ConversionFault(context, args, PevtType.Int, value, "结果超出 32 位有符号整数范围。");

                return (int)rounded;
            }

            if (int.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;

            throw ConversionFault(context, args, PevtType.Int, value, "文本结果不是合法 `int`。");
        }

        private static float ReadFloat(PevtRoutineContext context, PevtArguments args)
        {
            PevtQueryValue value = Read(context, args, PevtType.Float);

            if (value.Kind == PevtQueryValueKind.Number)
            {
                float converted = (float)value.Number;
                if (float.IsNaN(converted) || float.IsInfinity(converted))
                    throw ConversionFault(context, args, PevtType.Float, value, "结果不是有限值。");
                return converted;
            }

            if (float.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                return parsed;

            throw ConversionFault(context, args, PevtType.Float, value, "文本结果不是合法有限 `float`。");
        }

        /// <summary>
        /// 数值结果按"非零为真"转换——这正是原版只读查询表回报布尔时用的编码（1/0）。
        /// 文本结果只接受明确的真假拼写，不做"非空即真"这种猜测。
        /// </summary>
        private static bool ReadBool(PevtRoutineContext context, PevtArguments args)
        {
            PevtQueryValue value = Read(context, args, PevtType.Bool);

            if (value.Kind == PevtQueryValueKind.Number)
            {
                if (double.IsNaN(value.Number))
                    throw ConversionFault(context, args, PevtType.Bool, value, "结果是 NaN。");
                return value.Number != 0d;
            }

            string text = value.Text.Trim();
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1")
                return true;
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || text == "0")
                return false;

            throw ConversionFault(context, args, PevtType.Bool, value, "文本结果不是 `true`/`false`/`1`/`0`。");
        }

        private static string ReadString(PevtRoutineContext context, PevtArguments args)
        {
            PevtQueryValue value = Read(context, args, PevtType.String);

            if (value.Kind == PevtQueryValueKind.Text)
                return value.Text;

            // 数值键读成字符串是有意义的（"把当前危险度贴进对话文本"），用不受区域设置影响的往返格式。
            return value.Number.ToString("R", CultureInfo.InvariantCulture);
        }

        // ---- 诊断 ----

        private static List<string> QueryArguments(PevtArguments args)
        {
            var arguments = new List<string>(Math.Max(args.Count - 1, 0));
            for (int i = 1; i < args.Count; i++)
                arguments.Add(args.String(i));
            return arguments;
        }

        private static long Frame(PevtRoutineContext context) => context.Services.Clock?.Frame ?? 0L;

        private static PevtRoutineFailureException ConversionFault(
            PevtRoutineContext context, PevtArguments args, PevtType targetType, PevtQueryValue value, string reason)
        {
            string message = $"只读查询 `{args.String(0)}` 的结果 {value.Describe()} 无法转换成 {targetType.DisplayName()}：{reason}";

            // 成功的那条记录刚刚已经写进日志了：转换是在它之后失败的，因此这里再补一条失败记录，
            // 让 F8 上"读到了什么"和"为什么用不了"两件事都在。
            return Fault(context, "PEVTR4503", args.String(0), QueryArguments(args), targetType, value, message);
        }

        private static PevtRoutineFailureException Fault(
            PevtRoutineContext context,
            string diagnosticId,
            string key,
            IReadOnlyList<string> arguments,
            PevtType targetType,
            PevtQueryValue? value,
            string message,
            Exception innerException = null)
        {
            PevtGameQueryLog.Shared.Add(PevtGameQueryTrace.Failed(
                Frame(context), context.EventId, key, arguments, targetType, value, diagnosticId, message));

            return new PevtRoutineFailureException(diagnosticId, message, innerException);
        }
    }
}
