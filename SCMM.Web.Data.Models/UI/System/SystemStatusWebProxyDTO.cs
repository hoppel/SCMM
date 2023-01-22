﻿namespace SCMM.Web.Data.Models.UI.System;

public class SystemStatusWebProxyDTO
{
    public string Id { get; set; }

    public string Address { get; set; }

    public string CountryFlag { get; set; }

    public string CountryCode { get; set; }

    public string CityName { get; set; }

    public bool IsAvailable { get; set; }

    public DateTimeOffset LastCheckedOn { get; set; }

    public DateTimeOffset? LastUsedOn { get; set; }

    public int RequestSuccessCount { get; set; }

    public int RequestFailCount { get; set; }

    public IDictionary<string, DateTimeOffset> DomainRateLimits { get; set; }

    public SystemStatusSeverity Status
    {
        get
        {
            var now = DateTimeOffset.Now;
            if (!IsAvailable)
            {
                return SystemStatusSeverity.Critical;
            }
            else if (DomainRateLimits.Any(x => x.Value > now))
            {
                return SystemStatusSeverity.Degraded;
            }
            else
            {
                return SystemStatusSeverity.Normal;
            }
        }
    }

}
