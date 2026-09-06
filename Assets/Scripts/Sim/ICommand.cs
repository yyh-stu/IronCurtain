namespace IronCurtain.Sim
{
    // 命令模式:决策与执行解耦,保证同 Tick 内顺序确定
    public interface ICommand
    {
        void Execute(SimCore sim);
    }
}
