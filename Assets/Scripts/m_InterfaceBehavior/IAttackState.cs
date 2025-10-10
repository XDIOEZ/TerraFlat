using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltEvents;


    public interface IAttackState
    {
        UltEvent OnAttackStart { get; set; }
        UltEvent OnAttackUpdate { get; set; }
        UltEvent OnAttackEnd { get; set; }

        // Default implementation for starting an attack
        void StartAttack()
        {
            OnAttackStart?.Invoke();
        }

        // Default implementation for updating an attack
        void UpdateAttack()
        {
            OnAttackUpdate?.Invoke();
        }

        // Default implementation for ending an attack
        void EndAttack()
        {
            OnAttackEnd?.Invoke();
        }
    }

