// ���� IInteract �ӿڣ�����һ����������
public interface IInteract
{
    void Interact_Start(IInteracter interacter = null);
    //���ڽ���״̬
    void Interact_Update(IInteracter interacter = null);
    void Interact_Cancel(IInteracter interacter = null);
}