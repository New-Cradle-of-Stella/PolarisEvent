using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Loading;
using Xunit;

namespace Polaris.Event.Tests.Binding
{
    /// <summary>
    /// 15.6 节集合等待的绑定规则，最要紧的一条是"结果绑定声明的是新的普通变量"——不登记它们，
    /// 后面正常使用这些变量就会被误报成 PEVT6001。句柄来源用带返回值的 <c>async block</c> 而不是内置
    /// <c>_start</c> API，因为 P0 里可并行的 API 全是无返回值的。
    /// </summary>
    public class AggregateAwaitBinderTests
    {
        /// <summary>两个有 int 返回值的异步块，以及由它们得到的两个句柄。</summary>
        private const string TwoValueHandles =
            "async block _one() : int\nvar r1 : int = 1\nreturn r1\nendblock\n"
            + "async block _two() : int\nvar r2 : int = 2\nreturn r2\nendblock\n"
            + "handler a = _one()\n"
            + "handler b = _two()\n";

        /// <summary>一个无返回值的异步块，用来验证 PEVT7224。</summary>
        private const string OneVoidHandle =
            "async block _void()\nvar ignored : int = 1\nendblock\n"
            + "handler v = _void()\n";

        private static IReadOnlyList<string> Ids(string body)
        {
            PevtCompilation compilation = PevtSourceCompiler.Compile(
                new UTF8Encoding(false).GetBytes("id \"A\"\nenable async\n" + body + "end\n"),
                "aggregate.pevt",
                CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());

            return compilation.Diagnostics.Select(d => d.Id).ToList();
        }

        [Fact]
        public void BindingsBecomeUsableVariables()
        {
            // 回归：绑定变量以前没被声明，最后那一句会假报 PEVT6001。
            Assert.Empty(Ids(TwoValueHandles + "var n : int = await all(a, b)(x, y)\nvar m : int = x\n"));
        }

        [Fact]
        public void EmptyBindingListIsAllowed() =>
            Assert.Empty(Ids(TwoValueHandles + "var n : int = await any(a, b)()\n"));

        [Fact]
        public void DuplicateHandleReports7219() =>
            Assert.Contains("PEVT7219", Ids(TwoValueHandles + "var n : int = await all(a, a)(x, y)\n"));

        [Fact]
        public void BindingCountMismatchReports7221() =>
            Assert.Contains("PEVT7221", Ids(TwoValueHandles + "var n : int = await all(a, b)(x)\n"));

        [Fact]
        public void DuplicateBindingNameReports7223() =>
            Assert.Contains("PEVT7223", Ids(TwoValueHandles + "var n : int = await all(a, b)(x, x)\n"));

        [Fact]
        public void BindingNameClashingWithExistingVariableReports7223() =>
            Assert.Contains("PEVT7223", Ids(TwoValueHandles + "var x : int = 1\nvar n : int = await all(a, b)(x, y)\n"));

        /// <summary>句柄对应的异步定义没有普通返回值时不能绑定结果。</summary>
        [Fact]
        public void BindingVoidHandlerReports7224() =>
            Assert.Contains("PEVT7224", Ids(OneVoidHandle + "var n : int = await all(v)(x)\n"));

        [Fact]
        public void UndefinedHandleReports7218() =>
            Assert.Contains("PEVT7218", Ids("var n : int = await all(nope)(x)\n"));
    }
}
