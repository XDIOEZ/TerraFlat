// 定义 IInteract 接口，包含一个交互方法
public interface IInteract
{
    void Interact_Start(IInteracter interacter = null);
    //处于交互状态
    void Interact_Update(IInteracter interacter = null);
    void Interact_Cancel(IInteracter interacter = null);
}