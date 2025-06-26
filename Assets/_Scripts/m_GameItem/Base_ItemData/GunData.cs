using MemoryPack;

[MemoryPackable]
[System.Serializable]
public partial class GunData : Data_Weapon
{
    public float speed = 50f;
    public float fireRate = 10f;
    public float timeSinceLastFire = 0f;
    public FireMode WeaponFireMode;
    public GunType gunType;

    public enum FireMode
    {
        Single,
        Automatic,
        SemiAuto
    }
    public enum GunType
    {
        Pistol,
        Shotgun,
        SMG,
        Sniper,
        AutomaticRifle,
    }
}