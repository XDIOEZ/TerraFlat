namespace FlatWorld.Simulation
{
    /// <summary>供独立程序集读取的游戏模拟与表现时钟快照；写入由 Gameplay 时钟独占。</summary>
    public static class GameplaySimulationTime
    {
        #region 模拟时钟只读快照

        public static double CurrentTime { get; private set; }
        public static double PresentationTime { get; private set; }

        public static void Publish(double simulationTime, double presentationTime)
        {
            CurrentTime = simulationTime;
            PresentationTime = presentationTime;
        }

        #endregion
    }
}
