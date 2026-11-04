using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Lovatto.Crosshair
{
    public class bl_Outline : Shadow
    {
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!this.IsActive())
                return;

            var list = new List<UIVertex>();
            vh.GetUIVertexStream(list);

            ModifyVertices(list);

            vh.Clear();
            vh.AddUIVertexTriangleStream(list);
        }

        public virtual void ModifyVertices(List<UIVertex> verts)
        {
            if (!IsActive())
                return;

            var neededCapacity = verts.Count * 9;
            if (verts.Capacity < neededCapacity)
                verts.Capacity = neededCapacity;

            var original = verts.Count;
            var count = 0;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (!(x == 0 && y == 0))
                    {
                        var next = count + original;
                        ApplyShadowZeroAlloc(verts, effectColor, count, next, effectDistance.x * x, effectDistance.y * y);
                        count = next;
                    }
                }
            }
        }
    }
}