﻿namespace SCMM.Steam.Data.Models.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class BuyFromAttribute : Attribute
{
    public string Url { get; set; }

    public float FeeRate { get; set; }

    public long FeeSurcharge { get; set; }
}
