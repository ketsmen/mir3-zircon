namespace Client.Rendering
{
    public readonly struct RenderingStatistics
    {
        public readonly int DrawRequests;
        public readonly int D2DDraws;
        public readonly int D2DFlushes;
        public readonly int D3DDrawCalls;
        public readonly int D3DBatchFlushes;
        public readonly int D3DSprites;

        public RenderingStatistics(int drawRequests, int d2DDraws, int d2DFlushes, int d3DDrawCalls, int d3DBatchFlushes, int d3DSprites)
        {
            DrawRequests = drawRequests;
            D2DDraws = d2DDraws;
            D2DFlushes = d2DFlushes;
            D3DDrawCalls = d3DDrawCalls;
            D3DBatchFlushes = d3DBatchFlushes;
            D3DSprites = d3DSprites;
        }
    }
}
