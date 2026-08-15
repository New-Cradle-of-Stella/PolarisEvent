using System;
using System.Threading;

namespace Polaris.Pevt.Text
{
    /// <summary>
    /// 节流的取消检查。词法、解析等逐字符/逐 token 的热循环里每次都查询 <see cref="CancellationToken"/>
    /// 开销可观；本类型每隔固定步数才真正检查一次，同时保证请求取消后不超过一个 tick 周期就能感知。
    /// </summary>
    public struct CancellationGate
    {
        private readonly CancellationToken _token;
        private readonly int _interval;
        private int _counter;

        public CancellationGate(CancellationToken token, int interval = 1024)
        {
            if (interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "检查间隔必须为正数。");

            _token = token;
            _interval = interval;
            _counter = 0;
        }

        public void Tick()
        {
            _counter++;
            if (_counter < _interval)
                return;

            _counter = 0;
            _token.ThrowIfCancellationRequested();
        }
    }
}
