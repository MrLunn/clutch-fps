namespace ClutchFPS.Core
{
    public interface IDamageable
    {
        void TakeDamage(float amount, ulong attackerClientId);
    }
}
