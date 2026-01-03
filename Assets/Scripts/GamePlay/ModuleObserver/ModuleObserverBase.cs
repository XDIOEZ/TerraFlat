using MemoryPack;

[System.Serializable]
public abstract partial class ModuleObserverBase
{
    public virtual void OnInit(Module mod) { }
    public virtual void OnLoad(byte[] state) { }
    public virtual void OnUpdate(float timeDelta) { }
    public virtual byte[] OnSave(Module mod) { return null; }
}
