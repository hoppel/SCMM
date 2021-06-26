﻿using System;

namespace SCMM.Azure.ServiceBus.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ConcurrencyAttribute : Attribute
    {
        public int MaxConcurrentCalls { get; set; } = 1;
    }
}