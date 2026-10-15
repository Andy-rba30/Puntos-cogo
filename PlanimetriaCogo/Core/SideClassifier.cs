using System;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Convierte un offset con signo en lado, con tolerancia para "sobre el eje".</summary>
public static class SideClassifier
{
    public static Side Classify(double offset, double axisTolerance)
    {
        if (Math.Abs(offset) < axisTolerance)
            return Side.Center;
        return offset < 0 ? Side.Left : Side.Right;
    }
}
