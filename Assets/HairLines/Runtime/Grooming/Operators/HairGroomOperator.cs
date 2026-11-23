using System;
using System.Reflection;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public abstract class HairGroomOperator
    {
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public HairDensityMask Mask => _mask;
        internal int Seed => _seed;

        public abstract string DisplayName { get; }

        public virtual string Description => string.Empty;

        public virtual bool IsGlobal => false;

        public virtual int Priority => 0;

        [Tooltip("Turn the operator off without removing it from the stack.")]
        [SerializeField]
        private bool _enabled = true;

        [Tooltip("Where this operator acts. No mask means everywhere in the group.")]
        [SerializeField]
        private HairDensityMask _mask = new HairDensityMask();

        [Tooltip("Shifts the random pattern of this operator without touching the rest of the groom.")]
        [SerializeField]
        private int _seed = 1;

        public HairGroomOperator Clone()
        {
            var clone = (HairGroomOperator)MemberwiseClone();

            for (Type type = GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                                           BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType == typeof(HairDensityMask))
                    {
                        var source = (HairDensityMask)field.GetValue(this);
                        field.SetValue(clone, new HairDensityMask { Texture = source != null ? source.Texture : null });
                    }
                }
            }

            return clone;
        }

        public float WeightAt(in HairRootFrame root)
        {
            return _mask.IsUsable ? Mathf.Clamp01(_mask.Evaluate(root.Uv)) : 1f;
        }

        public void InvalidateMask()
        {
            _mask.Invalidate();
        }
    }
}
