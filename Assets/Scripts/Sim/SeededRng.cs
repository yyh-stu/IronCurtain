namespace IronCurtain.Sim
{
    // xorshift64*:无托管依赖、跨平台位级一致,禁用 System.Random 以保证可回放
    public sealed class SeededRng
    {
        private ulong _state;

        public SeededRng(ulong seed)
        {
            // 避免全零状态导致序列退化
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextUInt64()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 0x2545F4914F6CDD1DUL;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            // 用高位取模避免低位周期性;正数域运算防止负数取模偏差
            ulong range = (ulong)((long)maxExclusive - minInclusive);
            if (range == 0) return minInclusive;
            return minInclusive + (int)(NextUInt64() % range);
        }

        public double NextDouble01()
        {
            // 53 位保留 double 全精度
            return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
        }
    }
}
