using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Diagnostics
{
    public class DiagnosticBagTests
    {
        private static TextLocation SampleLocation()
        {
            var text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("abc"), "a.pevt").Text;
            return text.GetLocation(new TextSpan(0, 1));
        }

        [Fact]
        public void AddError_SetsHasErrors()
        {
            var bag = new DiagnosticBag();
            bag.AddError("PEVT1001", "test", SampleLocation());

            Assert.True(bag.HasErrors);
            Assert.Equal(1, bag.Count);
        }

        [Fact]
        public void AddWarning_DoesNotSetHasErrors()
        {
            var bag = new DiagnosticBag();
            bag.AddWarning("PEVT2301", "test", SampleLocation());

            Assert.False(bag.HasErrors);
            Assert.Equal(1, bag.Count);
        }

        [Fact]
        public void MixedSeverities_HasErrorsReflectsAnyError()
        {
            var bag = new DiagnosticBag();
            bag.AddWarning("PEVT2301", "warn", SampleLocation());
            bag.AddError("PEVT1001", "err", SampleLocation());

            Assert.True(bag.HasErrors);
            Assert.Equal(2, bag.Count);
        }

        [Fact]
        public void ToReadOnly_ReturnsIndependentSnapshot()
        {
            var bag = new DiagnosticBag();
            bag.AddError("PEVT1001", "first", SampleLocation());

            var snapshot = bag.ToReadOnly();
            bag.AddError("PEVT1002", "second", SampleLocation());

            Assert.Single(snapshot);
            Assert.Equal(2, bag.Count);
        }

        [Fact]
        public void Diagnostic_ToString_IncludesLocationAndId()
        {
            var diagnostic = new Diagnostic("PEVT1009", DiagnosticSeverity.Error, "bad encoding", SampleLocation());
            string text = diagnostic.ToString();

            Assert.Contains("PEVT1009", text);
            Assert.Contains("a.pevt", text);
        }
    }
}
