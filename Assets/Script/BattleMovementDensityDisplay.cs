using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class BattleMovementDensityDisplay
{
    // A plan view has one value per X/Z cell, not translucent height layers.
    public static Dictionary<Vector3Int,double> Project(BattleMovementDensity.Result source,
        float low, float high, bool allHeights)
    {
        var result=new Dictionary<Vector3Int,double>();
        foreach(var cell in source.seconds)
        {
            float y=cell.Key.y*source.verticalSize;
            if(!allHeights && (y<low || y>=high)) continue;
            var key=new Vector3Int(cell.Key.x,0,cell.Key.z);
            result.TryGetValue(key,out double previous);result[key]=previous+cell.Value;
        }
        return result;
    }
    // Fixed across altitude slices. Quantiles prevent a few spawn/camping hotspots
    // from forcing every other cell into the same low-contrast color.
    public static double[] Thresholds(IEnumerable<double> seconds)
    {
        var sorted=seconds.Where(x=>x>0 && !double.IsNaN(x) && !double.IsInfinity(x)).OrderBy(x=>x).ToArray();
        if(sorted.Length==0) return new double[5];
        var result=new double[5];
        for(int i=0;i<5;i++) result[i]=sorted[Math.Max(0,(int)Math.Ceiling(sorted.Length*(i+1)/5.0)-1)];
        return result;
    }
    public static int Band(double seconds,double[] thresholds)
    {
        for(int i=0;i<4;i++) if(seconds<=thresholds[i]) return i;
        return 4;
    }
}
